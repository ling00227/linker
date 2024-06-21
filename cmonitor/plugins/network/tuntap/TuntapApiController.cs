﻿using cmonitor.server;
using common.libs.api;
using cmonitor.plugins.tuntap.vea;
using cmonitor.client;
using cmonitor.plugins.tuntap.messenger;
using MemoryPack;
using cmonitor.config;
using common.libs.extends;
using cmonitor.client.capi;
using System.Collections.Concurrent;

namespace cmonitor.plugins.tuntap
{
    public sealed class TuntapClientApiController : IApiClientController
    {
        private readonly MessengerSender messengerSender;
        private readonly TuntapTransfer tuntapTransfer;
        private readonly ClientSignInState clientSignInState;
        private readonly Config config;

        public TuntapClientApiController(MessengerSender messengerSender, TuntapTransfer tuntapTransfer, ClientSignInState clientSignInState, Config config)
        {
            this.messengerSender = messengerSender;
            this.tuntapTransfer = tuntapTransfer;
            this.clientSignInState = clientSignInState;
            this.config = config;
        }
        /// <summary>
        /// 获取所有客户端的网卡信息
        /// </summary>
        /// <param name="param"></param>
        /// <returns></returns>
        public TuntabListInfo Get(ApiControllerParamsInfo param)
        {
            uint hashCode = uint.Parse(param.Content);
            uint _hashCode = tuntapTransfer.InfosVersion;
            if (_hashCode != hashCode)
            {
                return new TuntabListInfo
                {
                    List = tuntapTransfer.Infos,
                    HashCode = _hashCode
                };
            }
            return new TuntabListInfo { HashCode = _hashCode };
        }
        /// <summary>
        /// 刷新网卡信息
        /// </summary>
        /// <param name="param"></param>
        public void Refresh(ApiControllerParamsInfo param)
        {
            tuntapTransfer.Refresh();
        }

        /// <summary>
        /// 运行网卡
        /// </summary>
        /// <param name="param"></param>
        /// <returns></returns>
        public async Task<bool> Run(ApiControllerParamsInfo param)
        {
            //运行自己的
            if (param.Content == config.Data.Client.Id)
            {
                tuntapTransfer.Run();
            }
            else
            {
                //运行别人的
                await messengerSender.SendOnly(new MessageRequestWrap
                {
                    Connection = clientSignInState.Connection,
                    MessengerId = (ushort)TuntapMessengerIds.RunForward,
                    Payload = MemoryPackSerializer.Serialize(param.Content)
                });
            }
            return true;
        }
        /// <summary>
        /// 停止网卡
        /// </summary>
        /// <param name="param"></param>
        /// <returns></returns>
        public async Task<bool> Stop(ApiControllerParamsInfo param)
        {
            //停止自己的
            if (param.Content == config.Data.Client.Id)
            {
                tuntapTransfer.Stop();
            }
            else
            {
                //停止别人的
                await messengerSender.SendOnly(new MessageRequestWrap
                {
                    Connection = clientSignInState.Connection,
                    MessengerId = (ushort)TuntapMessengerIds.StopForward,
                    Payload = MemoryPackSerializer.Serialize(param.Content)
                });
            }
            return true;
        }

        //更新网卡信息
        public async Task<bool> Update(ApiControllerParamsInfo param)
        {
            TuntapInfo info = param.Content.DeJson<TuntapInfo>();
            //更新自己的
            if (info.MachineId == config.Data.Client.Id)
            {
                tuntapTransfer.OnUpdate(info);
            }
            else
            {
                //更新别人的
                await messengerSender.SendOnly(new MessageRequestWrap
                {
                    Connection = clientSignInState.Connection,
                    MessengerId = (ushort)TuntapMessengerIds.UpdateForward,
                    Payload = MemoryPackSerializer.Serialize(info)
                });
            }
            return true;
        }


        public sealed class TuntabListInfo
        {
            public ConcurrentDictionary<string, TuntapInfo> List { get; set; }
            public uint HashCode { get; set; }
        }
    }
}