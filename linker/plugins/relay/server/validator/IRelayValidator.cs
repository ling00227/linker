﻿using linker.config;
using linker.plugins.signin.messenger;
using RelayInfo = linker.plugins.relay.client.transport.RelayInfo;

namespace linker.plugins.relay.server.validator
{
    public interface IRelayValidator
    {
        /// <summary>
        /// 验证
        /// </summary>
        /// <param name="relayInfo">中继信息</param>
        /// <param name="fromMachine">来源客户端</param>
        /// <param name="toMachine">目标客户端，可能为null</param>
        /// <returns></returns>
        public Task<string> Validate(RelayInfo relayInfo, SignCacheInfo fromMachine, SignCacheInfo toMachine);
    }

    public sealed class RelayValidatorSecretKey : IRelayValidator
    {
        private readonly FileConfig fileConfig;

        public RelayValidatorSecretKey(FileConfig fileConfig)
        {
            this.fileConfig = fileConfig;
        }

        public async Task<string> Validate(RelayInfo relayInfo, SignCacheInfo fromMachine, SignCacheInfo toMachine)
        {
            if (relayInfo.SecretKey != fileConfig.Data.Server.Relay.SecretKey)
            {
                return $"SecretKey validate fail";
            }

            await Task.CompletedTask;
            return string.Empty;
        }
    }
}
