﻿using linker.config;
using linker.libs;
using linker.plugins.relay.server.caching;
using MemoryPack;
using System.Collections.Concurrent;
using System.Net;

namespace linker.plugins.relay.server
{
    public class RelayServerMasterTransfer
    {

        private ulong relayFlowingId = 0;


        private readonly IRelayCaching relayCaching;
        private readonly FileConfig fileConfig;

        private readonly ICrypto crypto;
        private readonly ConcurrentDictionary<string, RelayNodeReportInfo> reports = new ConcurrentDictionary<string, RelayNodeReportInfo>();

        public RelayServerMasterTransfer(IRelayCaching relayCaching, FileConfig fileConfig)
        {
            this.relayCaching = relayCaching;
            this.fileConfig = fileConfig;
            crypto = CryptoFactory.CreateSymmetric(fileConfig.Data.Server.Relay.Distributed.Master.SecretKey);
        }


        public ulong AddRelay(string fromid, string fromName, string toid, string toName, string groupid)
        {
            ulong flowingId = Interlocked.Increment(ref relayFlowingId);

            RelayCache cache = new RelayCache
            {
                FlowId = flowingId,
                FromId = fromid,
                FromName = fromName,
                ToId = toid,
                ToName = toName,
                GroupId = groupid
            };
            bool added = relayCaching.TryAdd($"{fromid}->{toid}->{flowingId}", cache, 15000);
            if (added == false) return 0;

            return flowingId;
        }

        public Memory<byte> TryGetRelayCache(string key)
        {
            if (relayCaching.TryGetValue(key, out RelayCache value))
            {
                byte[] bytes = crypto.Encode(MemoryPackSerializer.Serialize(value));
                return bytes;
            }
            return Helper.EmptyArray;
        }

        /// <summary>
        /// 设置节点
        /// </summary>
        /// <param name="ep"></param>
        /// <param name="data"></param>
        public void SetNodeReport(IPEndPoint ep, Memory<byte> data)
        {
            try
            {
                if (crypto == null) return;

                data = crypto.Decode(data.ToArray());
                RelayNodeReportInfo relayNodeReportInfo = MemoryPackSerializer.Deserialize<RelayNodeReportInfo>(data.Span);

                if (relayNodeReportInfo.Id == RelayNodeInfo.MASTER_NODE_ID)
                {
                    relayNodeReportInfo.EndPoint = new IPEndPoint(IPAddress.Any, 0);
                }
                else if (relayNodeReportInfo.EndPoint.Address.Equals(IPAddress.Any))
                {
                    relayNodeReportInfo.EndPoint.Address = ep.Address;
                }
                relayNodeReportInfo.LastTicks = Environment.TickCount64;
                reports.AddOrUpdate(relayNodeReportInfo.Id, relayNodeReportInfo, (a, b) => relayNodeReportInfo);
            }
            catch (Exception)
            {
            }
        }
        /// <summary>
        /// 获取节点列表
        /// </summary>
        /// <param name="validated">是否已认证</param>
        /// <returns></returns>
        public List<RelayNodeReportInfo> GetNodes(bool validated)
        {
            var result = reports.Values
                .Where(c => c.Public || (c.Public == false && validated))
                .Where(c => Environment.TickCount64 - c.LastTicks < 15000)
                .Where(c => c.ConnectionRatio < 100 && (c.MaxGbTotal == 0 || (c.MaxGbTotal > 0 && c.MaxGbTotalLastBytes > 0)))
                .OrderByDescending(c => c.LastTicks);

            return result.OrderByDescending(x => x.MaxConnection == 0 ? int.MaxValue : x.MaxConnection)
                     .ThenBy(x => x.ConnectionRatio)
                     .ThenBy(x => x.BandwidthRatio)
                     .ThenByDescending(x => x.MaxBandwidth == 0 ? double.MaxValue : x.MaxBandwidth)
                     .ThenByDescending(x => x.MaxBandwidthTotal == 0 ? double.MaxValue : x.MaxBandwidthTotal)
                     .ThenByDescending(x => x.MaxGbTotal == 0 ? double.MaxValue : x.MaxGbTotal)
                     .ThenByDescending(x => x.MaxGbTotalLastBytes == 0 ? ulong.MaxValue : x.MaxGbTotalLastBytes)
                     .ToList();
        }

        /// <summary>
        /// 是否需要认证
        /// </summary>
        /// <param name="nodeId"></param>
        /// <returns></returns>
        public bool NodeValidate(string nodeId)
        {
            return reports.TryGetValue(nodeId, out RelayNodeReportInfo relayNodeReportInfo) && relayNodeReportInfo.Public == false;
        }
    }


}
