﻿using linker.libs;
using linker.libs.extends;
using linker.libs.timer;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace linker.snat
{
    /// <summary>
    /// 64位，放x64的WinDivert.dll和WinDivert64.sys
    /// 32位，放x86的WinDivert.dll和WinDivert64.sys，WinDivert.sys
    /// 应用层简单SNAT
    /// 1，收到【客户端A】的数据包，10.18.18.23(客户端A的虚拟网卡IP)->192.168.56.6(局域网IP)
    /// 2，改为 192.168.56.2(本机IP)->192.168.56.6(局域网IP)
    /// 3，回来是 192.168.56.6(局域网IP)->192.168.56.2(本机IP)
    /// 4，改为 192.168.56.6(局域网IP)->10.18.18.23(客户端A的虚拟网卡IP)
    /// 5，回到客户端A，就完成了NAT
    /// </summary>
    public sealed class LinkerSrcNat
    {
        public bool Running => winDivert != null;

        /// <summary>
        /// 驱动
        /// </summary>
        private WinDivert winDivert;

        /// <summary>
        /// 网卡IP，用来作为源地址
        /// </summary>
        private NetworkIPv4Addr interfaceAddr;
        private HashSet<uint> interfaceAddrs;


        private uint srcIp;
        private NetworkIPv4Addr srcAddr;

        /// <summary>
        /// 用来注入数据包
        /// </summary>
        private WinDivertAddress addr = new WinDivertAddress
        {
            Layer = WinDivert.Layer.Network,
            Outbound = true,
            IPv6 = false
        };
        private CancellationTokenSource cts;

        /// <summary>
        /// 五元组NAT映射表
        /// </summary>
        private ConcurrentDictionary<(uint src, ushort srcPort, uint dst, ushort dstPort, ProtocolType pro), NatMapInfo> natMap = new ConcurrentDictionary<(uint src, ushort srcPort, uint dst, ushort dstPort, ProtocolType pro), NatMapInfo>();
        /// <summary>
        /// 分配端口表
        /// </summary>
        private ConcurrentDictionary<(uint src, ushort port), ushort> source2portMap = new ConcurrentDictionary<(uint src, ushort port), ushort>();

        public LinkerSrcNat()
        {
        }

        /// <summary>
        /// 启动
        /// </summary>
        /// <param name="info">启动参数</param>
        /// <param name="error">false启动失败的时候会有报错信息</param>
        /// <returns></returns>
        public bool Setup(SetupInfo info, out string error)
        {
            error = string.Empty;

            if (OperatingSystem.IsWindows() == false || (RuntimeInformation.ProcessArchitecture != Architecture.X86 && RuntimeInformation.ProcessArchitecture != Architecture.X64))
            {
                error = "only win x64 and win x86";
                return false;
            }
            if (info.Src == null)
            {
                error = "src is null，snat fail";
                return false;
            }
            if (info.Dsts == null || info.Dsts.Length == 0)
            {
                return false;
            }
            IPAddress defaultInterfaceIP = GetDefaultInterface();
            if (defaultInterfaceIP == null)
            {
                error = "SNAT get default interface id fail";
                string routes = CommandHelper.Windows(string.Empty, new string[] { $"route print" });
                LoggerHelper.Instance.Error(routes);
                return false;
            }
            interfaceAddrs = GetInterfaces();

            try
            {
                Shutdown();

                srcIp = NetworkHelper.ToValue(info.Src);
                srcAddr = IPv4Addr.Parse(info.Src.ToString());

                interfaceAddr = IPv4Addr.Parse(defaultInterfaceIP.ToString());
                string filters = BuildFilter(info.Dsts);
                winDivert = new WinDivert(filters, WinDivert.Layer.Network, 0, 0);

                cts = new CancellationTokenSource();

                Recv(cts);
                ClearTask(cts);

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            return false;
        }
        private IPAddress GetDefaultInterface()
        {
            string[] lines = CommandHelper.Windows(string.Empty, new string[] { $"route print" }).Split(Environment.NewLine);
            foreach (var item in lines)
            {
                if (item.Trim().StartsWith("0.0.0.0"))
                {
                    string[] arr = Regex.Replace(item.Trim(), @"\s+", " ").Split(' ');
                    return IPAddress.Parse(arr[arr.Length - 2]);
                }
            }
            return null;
        }
        private HashSet<uint> GetInterfaces()
        {
            return NetworkInterface.GetAllNetworkInterfaces().Select(c =>
            {
                try
                {
                    return c.GetIPProperties().UnicastAddresses.FirstOrDefault(c => c.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).Address;
                }
                catch (Exception)
                {
                }
                return null;
            }).Where(c => c != null).Select(NetworkHelper.ToValue).ToHashSet();
        }


        /// <summary>
        /// 过滤条件，只过滤一定的数据包
        /// </summary>
        /// <returns></returns>
        private string BuildFilter(AddrInfo[] dsts)
        {
            IEnumerable<string> ipRanges = dsts.Select(c => $"(ip.SrcAddr >= {c.NetworkIP} and ip.SrcAddr <= {c.BroadcastIP})");
            return $"inbound and ({string.Join(" or ", ipRanges)})";
            //return $"({string.Join(" or ", ipRanges)})";
        }
        /// <summary>
        /// 开始接收数据包
        /// </summary>
        private void Recv(CancellationTokenSource cts)
        {
            TimerHelper.Async(() =>
            {
                Memory<byte> packet = new Memory<byte>(new byte[10 * WinDivert.MTUMax]);
                Memory<WinDivertAddress> abuf = new Memory<WinDivertAddress>(new WinDivertAddress[10]);
                uint recvLen = 0, addrLen = 0;
                while (cts.IsCancellationRequested == false)
                {
                    try
                    {
                        (recvLen, addrLen) = winDivert.RecvEx(packet.Span, abuf.Span);

                        Memory<byte> recv = packet[..(int)recvLen];
                        Memory<WinDivertAddress> addr = abuf[..(int)addrLen];
                        foreach (var (i, p) in new WinDivertIndexedPacketParser(recv))
                        {
                            Recv(p, ref addr.Span[i]);
                        }
                        winDivert.SendEx(recv.Span, addr.Span);
                    }
                    catch (Exception)
                    {
                        break;
                    }
                }
                Shutdown();
            });
        }

        /// <summary>
        /// 还原数据包
        /// </summary>
        /// <param name="p"></param>
        /// <param name="addr"></param>
        /// <returns></returns>
        private unsafe bool Recv(WinDivertParseResult p, ref WinDivertAddress addr)
        {
            fixed (byte* ptr = p.Packet.Span)
            {
                byte ipHeaderLength = (byte)((*ptr & 0b1111) * 4);
                ProtocolType proto = (ProtocolType)p.IPv4Hdr->Protocol;

                bool result = (ProtocolType)p.IPv4Hdr->Protocol switch
                {
                    ProtocolType.Icmp => RecvIcmp(p, ptr),
                    ProtocolType.Tcp => RecvTcp(p, ptr),
                    ProtocolType.Udp => RecvUdp(p, ptr),
                    _ => false,
                };
                if (result)
                {
                    WinDivert.CalcChecksums(p.Packet.Span, ref addr, 0);
                }

                return result;
            }
        }
        /// <summary>
        /// 注入TCP/IP，让它直接走正确的网卡，路由到目的地
        /// </summary>
        /// <param name="packet">单个完整TCP/IP包</param>
        /// <returns>true注入成功，false失败了，你可以继续写入你的虚拟网卡</returns>
        public unsafe bool Inject(ReadOnlyMemory<byte> packet)
        {
            if (winDivert == null) return false;

            IPV4Packet ipv4 = new IPV4Packet(packet.Span);
            //不是 ipv4，是虚拟网卡ip，是广播，不nat
            if (ipv4.Version != 4 || ipv4.DstAddr == srcIp || ipv4.DstAddrSpan.GetIsBroadcastAddress())
            {
                return false;
            }
            fixed (byte* ptr = packet.Span)
            {
                foreach (var (i, p) in new WinDivertIndexedPacketParser(packet))
                {
                    //本机网卡IP不需要改，直接注入就可以
                    if (interfaceAddrs.Contains(ipv4.DstAddr) == false)
                    {
                        bool result = (ProtocolType)p.IPv4Hdr->Protocol switch
                        {
                            ProtocolType.Icmp => InjectIcmp(p, ptr),
                            ProtocolType.Tcp => InjectTcp(p, ptr),
                            ProtocolType.Udp => InjectUdp(p, ptr),
                            _ => false,
                        };
                        if (result == false) return false;

                        //改写源地址为网卡地址
                        p.IPv4Hdr->SrcAddr = interfaceAddr;
                    }
                    WinDivert.CalcChecksums(p.Packet.Span, ref addr, 0);
                    winDivert.SendEx(p.Packet.Span, new ReadOnlySpan<WinDivertAddress>(ref addr));
                }
            }
            return true;
        }

        /// <summary>
        /// 注入ICMP
        /// </summary>
        /// <param name="p"></param>
        /// <param name="ptr"></param>
        /// <returns></returns>
        private unsafe bool InjectIcmp(WinDivertParseResult p, byte* ptr)
        {
            //只操作response 和 request
            if (p.ICMPv4Hdr->Type != 0 && p.ICMPv4Hdr->Type != 8) return false;

            IPV4Packet ipv4 = new IPV4Packet(ptr);
            if (ipv4.IsFragment) return false;

            //原标识符，两个字节
            byte* ptr0 = ipv4.IcmpIdentifier0;
            byte* ptr1 = ipv4.IcmpIdentifier1;

            //用源地址的第三个，第四个字节作为新的标识符
            byte identifier0 = ipv4.SrcAddrSpan[2];
            byte identifier1 = ipv4.SrcAddrSpan[3];

            //保存，源地址。标识符0，目的地址，标识符1，ICMP
            //取值，目的地址，标识符0，源地址，标识符1，ICMP
            //因为回来的数据包，地址交换了
            ValueTuple<uint, ushort, uint, ushort, ProtocolType> key = (interfaceAddr.Raw, identifier0, p.IPv4Hdr->DstAddr.Raw, identifier1, ProtocolType.Icmp);

            if (natMap.TryGetValue(key, out NatMapInfo natMapInfo) == false)
            {
                natMapInfo = new NatMapInfo();
                natMap.TryAdd(key, natMapInfo);
            }
            natMapInfo.SrcAddr = p.IPv4Hdr->SrcAddr;
            natMapInfo.Identifier0 = *ptr0;
            natMapInfo.Identifier1 = *ptr1;
            natMapInfo.LastTime = Environment.TickCount64;
            natMapInfo.Timeout = 15 * 1000;

            //改写为新的标识符
            *ptr0 = identifier0;
            *ptr1 = identifier1;

            return true;
        }
        /// <summary>
        /// 还原ICMP
        /// </summary>
        /// <param name="p"></param>
        /// <param name="ptr"></param>
        /// <returns></returns>
        private unsafe bool RecvIcmp(WinDivertParseResult p, byte* ptr)
        {
            //只操作response 和 request
            if (p.ICMPv4Hdr->Type != 0 && p.ICMPv4Hdr->Type != 8) return false;

            IPV4Packet ipv4 = new IPV4Packet(ptr);

            //标识符，两个字节
            byte* ptr0 = ipv4.IcmpIdentifier0;
            byte* ptr1 = ipv4.IcmpIdentifier1;

            ValueTuple<uint, ushort, uint, ushort, ProtocolType> key = (p.IPv4Hdr->DstAddr.Raw, *ptr0, p.IPv4Hdr->SrcAddr.Raw, *ptr1, ProtocolType.Icmp);
            if (natMap.TryGetValue(key, out NatMapInfo natMapInfo))
            {
                //改回原来的标识符
                *ptr0 = natMapInfo.Identifier0;
                *ptr1 = natMapInfo.Identifier1;
                p.IPv4Hdr->DstAddr = natMapInfo.SrcAddr;
                return true;

            }
            return false;
        }
        /// <summary>
        /// 注入TCP
        /// </summary>
        /// <param name="p"></param>
        /// <param name="ptr"></param>
        /// <returns></returns>
        private unsafe bool InjectTcp(WinDivertParseResult p, byte* ptr)
        {
            IPV4Packet ipv4 = new IPV4Packet(ptr);

            //新端口
            ValueTuple<uint, ushort> portKey = (p.IPv4Hdr->SrcAddr.Raw, p.TCPHdr->SrcPort);
            if (source2portMap.TryGetValue(portKey, out ushort newPort) == false)
            {
                //只在syn时建立
                if (ipv4.TcpFlagSyn == false || ipv4.TcpFlagAck) return false;
                newPort = ApplyNewPort();
                source2portMap.TryAdd(portKey, newPort);
            }

            //添加映射
            ValueTuple<uint, ushort, uint, ushort, ProtocolType> key = (interfaceAddr.Raw, newPort, p.IPv4Hdr->DstAddr.Raw, p.TCPHdr->DstPort, ProtocolType.Tcp);
            if (natMap.TryGetValue(key, out NatMapInfo natMapInfo) == false)
            {
                natMapInfo = new NatMapInfo
                {
                    SrcAddr = p.IPv4Hdr->SrcAddr,
                    SrcPort = p.TCPHdr->SrcPort,
                    LastTime = Environment.TickCount64,
                    Timeout = 2 * 60 * 60 * 1000 //tcp 2小时
                };
                natMap.TryAdd(key, natMapInfo);
            }
            natMapInfo.LastTime = Environment.TickCount64;
            //fin+ack 或者 rst 就清除
            if (ipv4.TcpFlagFin) natMapInfo.Fin0 = ipv4.TcpFlagFin;
            if (ipv4.TcpFlagRst) natMapInfo.Rst = ipv4.TcpFlagRst;
            if (natMapInfo.Fin0 && ipv4.TcpFlagAck) natMapInfo.FinAck = ipv4.TcpFlagAck;

            p.TCPHdr->SrcPort = newPort;
            return true;
        }
        /// <summary>
        /// 还原TCP
        /// </summary>
        /// <param name="p"></param>
        /// <param name="ptr"></param>
        /// <returns></returns>
        private unsafe bool RecvTcp(WinDivertParseResult p, byte* ptr)
        {
            IPV4Packet ipv4 = new IPV4Packet(ptr);

            ValueTuple<uint, ushort, uint, ushort, ProtocolType> key = (p.IPv4Hdr->DstAddr.Raw, p.TCPHdr->DstPort, p.IPv4Hdr->SrcAddr.Raw, p.TCPHdr->SrcPort, ProtocolType.Tcp);
            if (natMap.TryGetValue(key, out NatMapInfo natMapInfo))
            {
                natMapInfo.LastTime = Environment.TickCount64;

                //fin+ack 或者 rst 就清除
                if (ipv4.TcpFlagFin) natMapInfo.Fin1 = ipv4.TcpFlagFin;
                if (ipv4.TcpFlagRst) natMapInfo.Rst = ipv4.TcpFlagRst;
                if (natMapInfo.Fin1 && ipv4.TcpFlagAck) natMapInfo.FinAck = ipv4.TcpFlagAck;

                p.IPv4Hdr->DstAddr = natMapInfo.SrcAddr;
                p.TCPHdr->DstPort = natMapInfo.SrcPort;
                return true;
            }
            return false;
        }
        /// <summary>
        /// 注入UDP
        /// </summary>
        /// <param name="p"></param>
        /// <param name="ptr"></param>
        /// <returns></returns>
        private unsafe bool InjectUdp(WinDivertParseResult p, byte* ptr)
        {
            //新端口
            ValueTuple<uint, ushort> portKey = (p.IPv4Hdr->SrcAddr.Raw, p.UDPHdr->SrcPort);
            if (source2portMap.TryGetValue(portKey, out ushort newPort) == false)
            {
                newPort = ApplyNewPort();
                source2portMap.TryAdd(portKey, newPort);
            }
            //映射
            ValueTuple<uint, ushort, uint, ushort, ProtocolType> key = (interfaceAddr.Raw, newPort, p.IPv4Hdr->DstAddr.Raw, p.UDPHdr->DstPort, ProtocolType.Tcp);
            if (natMap.TryGetValue(key, out NatMapInfo natMapInfo) == false)
            {
                natMapInfo = new NatMapInfo
                {
                    SrcAddr = p.IPv4Hdr->SrcAddr,
                    SrcPort = p.UDPHdr->SrcPort,
                    LastTime = Environment.TickCount64,
                    Timeout = 30 * 60 * 1000 //UDP 30分钟
                };
                natMap.TryAdd(key, natMapInfo);
            }
            natMapInfo.LastTime = Environment.TickCount64;

            p.UDPHdr->SrcPort = newPort;
            return true;
        }
        /// <summary>
        /// 还原UDP
        /// </summary>
        /// <param name="p"></param>
        /// <param name="ptr"></param>
        /// <returns></returns>
        private unsafe bool RecvUdp(WinDivertParseResult p, byte* ptr)
        {
            ValueTuple<uint, ushort, uint, ushort, ProtocolType> key = (p.IPv4Hdr->DstAddr.Raw, p.UDPHdr->DstPort, p.IPv4Hdr->SrcAddr.Raw, p.UDPHdr->SrcPort, ProtocolType.Tcp);
            if (natMap.TryGetValue(key, out NatMapInfo natMapInfo))
            {
                natMapInfo.LastTime = Environment.TickCount64;
                p.IPv4Hdr->DstAddr = natMapInfo.SrcAddr;
                p.UDPHdr->DstPort = natMapInfo.SrcPort;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 申请一个新的端口
        /// </summary>
        /// <returns></returns>
        private ushort ApplyNewPort()
        {
            using Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(IPAddress.Any, 0));

            return (ushort)(socket.LocalEndPoint as IPEndPoint).Port;
        }

        /// <summary>
        /// 关闭
        /// </summary>
        public void Shutdown()
        {
            cts?.Cancel();

            winDivert?.Dispose();
            winDivert = null;

            natMap.Clear();
            source2portMap.Clear();
        }

        private void ClearTask(CancellationTokenSource cts)
        {
            TimerHelper.SetIntervalLong(() =>
            {
                long now = Environment.TickCount64;
                foreach (var item in natMap.Where(c => now - c.Value.LastTime > c.Value.Timeout || c.Value.FinAck || c.Value.Rst).Select(c => c.Key).ToList())
                {
                    if (natMap.TryRemove(item, out NatMapInfo natMapInfo))
                    {
                        source2portMap.TryRemove((natMapInfo.SrcAddr.Raw, natMapInfo.SrcPort), out _);
                    }
                }
                return cts.IsCancellationRequested == false;
            }, 5000);
        }
        public sealed class AddrInfo
        {
            public AddrInfo(IPAddress ip, byte prefixLength)
            {
                IP = ip;
                PrefixLength = prefixLength;

                PrefixValue = NetworkHelper.ToPrefixValue(PrefixLength);
                NetworkValue = NetworkHelper.ToNetworkValue(IP, PrefixLength);
                BroadcastValue = NetworkHelper.ToBroadcastValue(IP, PrefixLength);

                Addr = IPv4Addr.Parse(IP.ToString());
                NetworkAddr = IPv4Addr.Parse(NetworkHelper.ToIP(NetworkValue).ToString());

                NetworkIP = NetworkHelper.ToIP(NetworkValue);
                BroadcastIP = NetworkHelper.ToIP(BroadcastValue);
            }
            public IPAddress IP { get; }
            public byte PrefixLength { get; }

            public NetworkIPv4Addr Addr { get; private set; }
            public NetworkIPv4Addr NetworkAddr { get; private set; }

            public uint PrefixValue { get; private set; }
            public uint NetworkValue { get; private set; }
            public uint BroadcastValue { get; private set; }

            public IPAddress NetworkIP { get; private set; }
            public IPAddress BroadcastIP { get; private set; }
        }
        public sealed class SetupInfo
        {
            /// <summary>
            /// 虚拟网卡IP
            /// </summary>
            public IPAddress Src { get; init; }
            /// <summary>
            /// 需要NAT的IP
            /// </summary>
            public AddrInfo[] Dsts { get; init; }
        }

        /// <summary>
        /// NAT映射记录
        /// </summary>
        sealed class NatMapInfo
        {
            //IP头
            public NetworkIPv4Addr SrcAddr { get; set; }

            //TCP/UDP
            public NetworkUInt16 SrcPort { get; set; }

            //ICMP
            public byte Identifier0 { get; set; }
            public byte Identifier1 { get; set; }

            //TCP
            public bool Fin0 { get; set; }
            public bool Fin1 { get; set; }
            public bool FinAck { get; set; }
            public bool Rst { get; set; }

            public long LastTime { get; set; } = Environment.TickCount64;
            public int Timeout { get; set; } = 1 * 60 * 60;
        }
        /// <summary>
        /// IPV4 包
        /// </summary>
        public unsafe struct IPV4Packet
        {
            byte* ptr;

            /// <summary>
            /// 协议版本
            /// </summary>
            public byte Version => (byte)((*ptr >> 4) & 0b1111);
            public ProtocolType Protocol => (ProtocolType)(*(ptr + 9));



            /// <summary>
            /// 源地址
            /// </summary>
            public uint SrcAddr => BinaryPrimitives.ReverseEndianness(*(uint*)(ptr + 12));
            /// <summary>
            /// 源端口
            /// </summary>
            public ushort SrcPort => BinaryPrimitives.ReverseEndianness(*(ushort*)(ptr + IPHeadLength));
            /// <summary>
            /// 目的地址
            /// </summary>
            public uint DstAddr => BinaryPrimitives.ReverseEndianness(*(uint*)(ptr + 16));
            /// <summary>
            /// 目标端口
            /// </summary>
            public ushort DstPort => BinaryPrimitives.ReverseEndianness(*(ushort*)(ptr + IPHeadLength + 2));
            /// <summary>
            /// 源地址
            /// </summary>
            public ReadOnlySpan<byte> SrcAddrSpan => new Span<byte>((ptr + 12), 4);
            /// <summary>
            /// 目的地址
            /// </summary>
            public ReadOnlySpan<byte> DstAddrSpan => new Span<byte>((ptr + 16), 4);

            /// <summary>
            /// IP头长度
            /// </summary>
            public int IPHeadLength => (*ptr & 0b1111) * 4;

            /// <summary>
            /// IP Flag
            /// </summary>
            public byte Flag => (byte)(*(ptr + 6) >> 5);
            /// <summary>
            /// 不分片
            /// </summary>
            public bool DontFragment => (Flag & 0x02) == 2;
            /// <summary>
            /// 更多分片
            /// </summary>
            public bool MoreFragment => (Flag & 0x01) == 1;
            /// <summary>
            /// 分片偏移量
            /// </summary>
            public ushort Offset => (ushort)(BinaryPrimitives.ReverseEndianness(*(ushort*)(ptr + 6)) & 0x1fff);
            /// <summary>
            /// 是否分片
            /// </summary>
            public bool IsFragment => MoreFragment || Offset > 0;

            /// <summary>
            /// ICMP标志第一个字节
            /// </summary>
            public byte* IcmpIdentifier0 => ptr + IPHeadLength + 4;
            /// <summary>
            /// ICMP标志第二个字节
            /// </summary>
            public byte* IcmpIdentifier1 => ptr + IPHeadLength + 5;

            /// <summary>
            /// TCP Flag
            /// </summary>
            public byte TcpFlag => *(ptr + IPHeadLength + 13);
            public bool TcpFlagFin => (TcpFlag & 0b000001) != 0;
            public bool TcpFlagSyn => (TcpFlag & 0b000010) != 0;
            public bool TcpFlagRst => (TcpFlag & 0b000100) != 0;
            public bool TcpFlagPsh => (TcpFlag & 0b001000) != 0;
            public bool TcpFlagAck => (TcpFlag & 0b010000) != 0;
            public bool TcpFlagUrg => (TcpFlag & 0b100000) != 0;

            public IPV4Packet(byte* ptr)
            {
                this.ptr = ptr;
            }
            public IPV4Packet(ReadOnlySpan<byte> span)
            {
                fixed (byte* ptr = span)
                {
                    this.ptr = ptr;
                }
            }
        }

        /// <summary>
        /// SNAT回调
        /// </summary>
        public interface ILinkerSNatRecvCallback
        {
            /// <summary>
            /// 接收到的TCP/IP数据包
            /// </summary>
            /// <param name="packet"></param>
            public void Recv(ReadOnlyMemory<byte> packet);
        }
    }
}
