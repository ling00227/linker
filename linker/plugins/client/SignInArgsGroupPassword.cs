﻿using linker.plugins.signIn.args;
using linker.plugins.signin.messenger;
using linker.config;

namespace linker.plugins.client
{
    /// <summary>
    /// 添加分组密码
    /// </summary>
    public sealed class SignInArgsGroupPasswordClient : ISignInArgs
    {
        private readonly FileConfig fileConfig;
        public SignInArgsGroupPasswordClient(FileConfig fileConfig)
        {
            this.fileConfig = fileConfig;
        }
        public async Task<string> Invoke(string host, Dictionary<string, string> args)
        {
            args.TryAdd("signin-gpwd", fileConfig.Data.Client.Group.Password);
            await Task.CompletedTask;
            return string.Empty;
        }

        public async Task<string> Validate(SignInfo signInfo, SignCacheInfo cache)
        {
            await Task.CompletedTask;
            return string.Empty;
        }
    }

    /// <summary>
    /// 验证分组密码
    /// </summary>
    public sealed class SignInArgsGroupPasswordServer : ISignInArgs
    {
        private readonly FileConfig fileConfig;
        public SignInArgsGroupPasswordServer(FileConfig fileConfig)
        {
            this.fileConfig = fileConfig;
        }
        public async Task<string> Invoke(string host, Dictionary<string, string> args)
        {
            await Task.CompletedTask;
            return string.Empty;
        }

        /// <summary>
        /// 验证参数
        /// </summary>
        /// <param name="signInfo">新登录参数</param>
        /// <param name="cache">之前的登录信息</param>
        /// <returns></returns>
        public async Task<string> Validate(SignInfo signInfo, SignCacheInfo cache)
        {
            if (string.IsNullOrWhiteSpace(fileConfig.Data.Server.SignIn.SecretKey) == false)
            {
                if (signInfo.Args.TryGetValue("signin-gpwd", out string gpwd) && string.IsNullOrWhiteSpace(gpwd) == false)
                {
                    signInfo.GroupId = $"{signInfo.GroupId}->{gpwd}";
                }
            }
            await Task.CompletedTask;
            return string.Empty;
        }


    }
}