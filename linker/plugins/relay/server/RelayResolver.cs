﻿using System.Net.Sockets;
using System.Buffers;
using linker.libs.extends;
using System.Collections.Concurrent;
using linker.plugins.resolver;
using System.Net;
using MemoryPack;
using linker.config;
using linker.libs;

namespace linker.plugins.relay.server
{
    /// <summary>
    /// 中继连接处理
    /// </summary>
    public class RelayResolver : IResolver
    {
        public ResolverType Type => ResolverType.Relay;

        private readonly RelayServerNodeTransfer relayServerNodeTransfer;

        public RelayResolver(RelayServerNodeTransfer relayServerNodeTransfer)
        {
            this.relayServerNodeTransfer = relayServerNodeTransfer;
        }

        private readonly ConcurrentDictionary<ulong, RelayWrap> relayDic = new ConcurrentDictionary<ulong, RelayWrap>();


        public virtual void AddReceive(string key, string from, string to, string groupid, ulong bytes)
        {
        }
        public virtual void AddSendt(string key, string from, string to, string groupid, ulong bytes)
        {
        }
        public virtual void AddReceive(string key, ulong bytes)
        {
        }
        public virtual void AddSendt(string key, ulong bytes)
        {
        }


        public async Task Resolve(Socket socket, IPEndPoint ep, Memory<byte> memory)
        {
            await Task.CompletedTask;
        }
        public async Task Resolve(Socket socket, Memory<byte> memory)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(1024);
            try
            {
                int length = await socket.ReceiveAsync(buffer.AsMemory(), SocketFlags.None).ConfigureAwait(false);
                RelayMessage relayMessage = MemoryPackSerializer.Deserialize<RelayMessage>(buffer.AsMemory(0, length).Span);

                if (relayMessage.Type == RelayMessengerType.Ask && relayMessage.NodeId != RelayNodeInfo.MASTER_NODE_ID)
                {
                    if (relayServerNodeTransfer.Validate() == false)
                    {
                        if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                            LoggerHelper.Instance.Error($"relay {relayMessage.Type} Validate false,flowid:{relayMessage.FlowId}");
                        await socket.SendAsync(new byte[] { 1 });
                        socket.SafeClose();
                        return;
                    }
                }
                //ask 是发起端来的，那key就是 发起端->目标端， answer的，目标和来源会交换，所以转换一下
                string key = relayMessage.Type == RelayMessengerType.Ask ? $"{relayMessage.FromId}->{relayMessage.ToId}->{relayMessage.FlowId}" : $"{relayMessage.ToId}->{relayMessage.FromId}->{relayMessage.FlowId}";
                //获取缓存
                RelayCache relayCache = await relayServerNodeTransfer.TryGetRelayCache(key, relayMessage.NodeId);
                if (relayCache == null)
                {
                    if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                        LoggerHelper.Instance.Error($"relay {relayMessage.Type} get cache fail,flowid:{relayMessage.FlowId}");
                    socket.SafeClose();
                    return;
                }
                //流量统计
                AddReceive(relayCache.FromId, relayCache.FromName, relayCache.ToName, relayCache.GroupId, (ulong)length);
                try
                {
                    switch (relayMessage.Type)
                    {
                        case RelayMessengerType.Ask:
                            {
                                //添加本地缓存
                                RelayWrap relayWrap = new RelayWrap { Socket = socket, Tcs = new TaskCompletionSource<Socket>() };
                                relayWrap.Limit.SetLimit(relayServerNodeTransfer.GetBandwidthLimit());

                                relayDic.TryAdd(relayCache.FlowId, relayWrap);

                                await socket.SendAsync(new byte[] { 0 });

                                //等待对方连接
                                Socket targetSocket = await relayWrap.Tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(15000));
                                _ = CopyToAsync(relayCache, relayWrap.Limit, socket, targetSocket);
                            }
                            break;
                        case RelayMessengerType.Answer:
                            {
                                //看发起端缓存
                                if (relayDic.TryRemove(relayCache.FlowId, out RelayWrap relayWrap) == false || relayWrap.Socket == null)
                                {
                                    if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                                        LoggerHelper.Instance.Error($"relay {relayMessage.Type} get cache fail,flowid:{relayMessage.FlowId}");
                                    socket.SafeClose();
                                    return;
                                }
                                //告诉发起端我的socket
                                relayWrap.Tcs.SetResult(socket);
                                _ = CopyToAsync(relayCache, relayWrap.Limit, socket, relayWrap.Socket);
                            }
                            break;
                        default:
                            {
                                if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                                    LoggerHelper.Instance.Error($"relay {relayMessage.Type}  unknow type,flowid:{relayMessage.FlowId}");
                                socket.SafeClose();
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                        LoggerHelper.Instance.Error($"{ex},flowid:{relayMessage.FlowId}");
                    if (relayDic.TryRemove(relayCache.FlowId, out RelayWrap remove))
                    {
                        remove.Socket?.SafeClose();
                    }
                }
            }
            catch (Exception ex)
            {
                if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                    LoggerHelper.Instance.Error(ex);
                socket.SafeClose();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        private async Task CopyToAsync(RelayCache cache, RelaySpeedLimit limit, Socket source, Socket destination)
        {
            byte[] buffer = new byte[4 * 1024];
            try
            {
                relayServerNodeTransfer.IncrementConnectionNum();
                int bytesRead;
                while ((bytesRead = await source.ReceiveAsync(buffer.AsMemory()).ConfigureAwait(false)) != 0)
                {
                    //流量限制
                    if (relayServerNodeTransfer.AddBytes((ulong)bytesRead) == false)
                    {
                        source.SafeClose();
                        break;
                    }

                    //总速度
                    if (relayServerNodeTransfer.NeedLimit())
                    {
                        int length = bytesRead;
                        relayServerNodeTransfer.TryLimit(ref length);
                        while (length > 0)
                        {
                            await Task.Delay(30).ConfigureAwait(false);
                            relayServerNodeTransfer.TryLimit(ref length);
                        }
                    }
                    //单个速度
                    if (limit.NeedLimit())
                    {
                        int length = bytesRead;
                        limit.TryLimit(ref length);
                        while (length > 0)
                        {
                            await Task.Delay(30).ConfigureAwait(false);
                            limit.TryLimit(ref length);
                        }
                    }

                    AddReceive(cache.FromId, cache.FromName, cache.ToName, cache.GroupId, (ulong)bytesRead);
                    AddSendt(cache.FromId, cache.FromName, cache.ToName, cache.GroupId, (ulong)bytesRead);
                    await destination.SendAsync(buffer.AsMemory(0, bytesRead)).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                relayServerNodeTransfer.DecrementConnectionNum();
            }
        }

    }


    public enum RelayMessengerType : byte
    {
        Ask = 0,
        Answer = 1,
    }

    public class RelaySpeedLimit
    {
        private uint relayLimit = 0;
        private double relayLimitToken = 0;
        private double relayLimitBucket = 0;
        private long relayLimitTicks = Environment.TickCount64;

        public bool NeedLimit()
        {
            return relayLimit > 0;
        }
        public void SetLimit(uint bytes)
        {
            relayLimit = bytes;
            relayLimitToken = relayLimit / 1000.0;
            relayLimitBucket = relayLimit;
        }
        public bool TryLimit(ref int length)
        {
            if (relayLimit == 0) return false;

            lock (this)
            {
                long _relayLimitTicks = Environment.TickCount64;
                long relayLimitTicksTemp = _relayLimitTicks - relayLimitTicks;
                relayLimitTicks = _relayLimitTicks;
                relayLimitBucket += relayLimitTicksTemp * relayLimitToken;
                if (relayLimitBucket > relayLimit) relayLimitBucket = relayLimit;

                if (relayLimitBucket >= length)
                {
                    relayLimitBucket -= length;
                    length = 0;
                }
                else
                {
                    length -= (int)relayLimitBucket;
                    relayLimitBucket = 0;
                }
            }
            return true;
        }
    }

    [MemoryPackable]
    public sealed partial class RelayCache
    {
        public ulong FlowId { get; set; }
        public string FromId { get; set; }
        public string FromName { get; set; }
        public string ToId { get; set; }
        public string ToName { get; set; }
        public string GroupId { get; set; }
    }

    public sealed class RelayWrap
    {
        public TaskCompletionSource<Socket> Tcs { get; set; }
        public Socket Socket { get; set; }

        [MemoryPackIgnore]
        public RelaySpeedLimit Limit { get; set; } = new RelaySpeedLimit();
    }

    [MemoryPackable]
    public sealed partial class RelayMessage
    {
        public RelayMessengerType Type { get; set; }
        public ulong FlowId { get; set; }
        public string FromId { get; set; }
        public string ToId { get; set; }

        public string NodeId { get; set; }
    }
}