﻿using linker.libs;
using linker.messenger.api;
using linker.messenger.decenter;
using linker.messenger.signin;
using System.Collections;
using System.Collections.Concurrent;

namespace linker.messenger.access
{
    public sealed class AccessDecenter : IDecenter
    {
        public string Name => "access";
        public VersionManager PushVersion { get; } = new VersionManager();
        public VersionManager DataVersion { get; } = new VersionManager();

        /// <summary>
        /// 各个设备的权限列表
        /// </summary>
        public ConcurrentDictionary<string, BitArray> Accesss { get; } = new ConcurrentDictionary<string, BitArray>();

        private readonly ISignInClientStore signInClientStore;
        private readonly IAccessStore accessStore;
        private readonly ISerializer serializer;
        public AccessDecenter(SignInClientState signInClientState, ISignInClientStore signInClientStore, IAccessStore accessStore, ISerializer serializer)
        {
            this.signInClientStore = signInClientStore;
            this.accessStore = accessStore;
            this.serializer = serializer;

            signInClientState.OnSignInSuccess += (times) => PushVersion.Increment();
            accessStore.OnChanged += PushVersion.Increment;
           
        }
        /// <summary>
        /// 刷新同步
        /// </summary>
        public void Refresh()
        {
            PushVersion.Increment();
        }
        public Memory<byte> GetData()
        {
            return serializer.Serialize(new AccessBitsInfo { MachineId = signInClientStore.Id, Access = accessStore.AccessBits });
        }
        public void AddData(Memory<byte> data)
        {
            AccessBitsInfo access = serializer.Deserialize<AccessBitsInfo>(data.Span);
            Accesss.AddOrUpdate(access.MachineId, access.Access, (a, b) => access.Access);
        }
        public void AddData(List<ReadOnlyMemory<byte>> data)
        {
            List<AccessBitsInfo> list = data.Select(c => serializer.Deserialize<AccessBitsInfo>(c.Span)).ToList();
            foreach (var item in list)
            {
                Accesss.AddOrUpdate(item.MachineId, item.Access, (a, b) => item.Access);
            }
        }
        public void ClearData()
        {
            Accesss.Clear();
        }
        public void ProcData()
        {
        }
    }
}
