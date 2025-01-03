﻿using linker.store;
using linker.libs;
using LiteDB;
using MemoryPack;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Serialization;
using linker.plugins.messenger;
using linker.plugins.signIn.args;

namespace linker.plugins.signin.messenger
{
    public sealed class SignCaching
    {
        private readonly Storefactory dBfactory;
        private readonly ILiteCollection<SignCacheInfo> liteCollection;
        private readonly SignInArgsTransfer signInArgsTransfer;

        public ConcurrentDictionary<string, SignCacheInfo> Clients { get; set; } = new ConcurrentDictionary<string, SignCacheInfo>();

        public SignCaching(Storefactory dBfactory, SignInArgsTransfer signInArgsTransfer)
        {
            this.dBfactory = dBfactory;
            liteCollection = dBfactory.GetCollection<SignCacheInfo>("signs");

            this.signInArgsTransfer = signInArgsTransfer;

            foreach (var item in liteCollection.FindAll())
            {
                item.Connected = false;
                Clients.TryAdd(item.MachineId, item);
            }
            ClearTask();
        }

        public async Task<string> Sign(SignInfo signInfo)
        {
            if (string.IsNullOrWhiteSpace(signInfo.MachineId))
            {
                signInfo.MachineId = ObjectId.NewObjectId().ToString();
            }

            bool has = Clients.TryGetValue(signInfo.MachineId, out SignCacheInfo cache);
            if (has == false)
            {
                cache = new SignCacheInfo();
            }

            //参数验证失败
            string verifyResult = await signInArgsTransfer.Verify(signInfo, cache);
            if (string.IsNullOrWhiteSpace(verifyResult) == false)
            {
                cache.Connected = false;
                return verifyResult;
            }
            //无限制，则挤压下线
            cache.Connection?.Disponse(9);
            if (has == false)
            {
                cache.Id = new ObjectId(signInfo.MachineId);
                cache.MachineId = signInfo.MachineId;
                liteCollection.Insert(cache);
                Clients.TryAdd(signInfo.MachineId, cache);
            }

            signInfo.Connection.Id = signInfo.MachineId;
            signInfo.Connection.Name = signInfo.MachineName;
            cache.MachineName = signInfo.MachineName;
            cache.Connection = signInfo.Connection;
            cache.Version = signInfo.Version;
            cache.Args = signInfo.Args;
            cache.GroupId = signInfo.GroupId;
            liteCollection.Update(cache);
            dBfactory.Confirm();

            return string.Empty;
        }

        public bool TryGet(string machineId, out SignCacheInfo cache)
        {
            if (machineId == null)
            {
                cache = null;
                return false;
            }
            return Clients.TryGetValue(machineId, out cache);
        }

        public List<SignCacheInfo> Get()
        {
            return Clients.Values.ToList();
        }
        public List<SignCacheInfo> Get(string groupId)
        {
            return Clients.Values.Where(c => c.GroupId == groupId).ToList();
        }

        public bool GetOnline(string machineId)
        {
            return Clients.TryGetValue(machineId, out SignCacheInfo cache) && cache.Connected;
        }

        public void GetOnline(out int all, out int online)
        {
            all = Clients.Count;
            online = Clients.Values.Count(c => c.Connected);
        }

        public bool TryRemove(string machineId, out SignCacheInfo cache)
        {
            if (Clients.TryRemove(machineId, out cache))
            {
                liteCollection.Delete(cache.Id);
                dBfactory.Confirm();
            }
            return true;
        }


        private void ClearTask()
        {
            TimerHelper.SetInterval(() =>
            {
                if (LoggerHelper.Instance.LoggerLevel <= LoggerTypes.DEBUG)
                {
                    LoggerHelper.Instance.Debug($"start cleaning up clients that have exceeded the 7-day timeout period");
                }

                try
                {
                    DateTime now = DateTime.Now;

                    var groups = Clients.Values.GroupBy(c => c.GroupId)
                     .Where(group => group.All(info => info.Connected == false && (now - info.LastSignIn).TotalDays > 7))
                     .Select(group => group.Key).ToList();

                    if (groups.Count > 0)
                    {
                        var items = Clients.Values.Where(c => groups.Contains(c.GroupId)).ToList();

                        foreach (var item in items)
                        {
                            Clients.TryRemove(item.MachineId, out _);
                            liteCollection.Delete(item.Id);
                        }
                        dBfactory.Confirm();
                    }
                }
                catch (Exception ex)
                {
                    LoggerHelper.Instance.Debug($"cleaning up clients error {ex}");
                }

                return true;
            }, 5 * 60 * 1000);
        }
    }


    [MemoryPackable]
    public sealed partial class SignCacheInfo
    {
        [MemoryPackIgnore]
        public ObjectId Id { get; set; }

        public string MachineId { get; set; }
        public string MachineName { get; set; }
        public string Version { get; set; } = "v1.0.0";
        public string GroupId { get; set; } = Helper.GlobalString;
        public DateTime LastSignIn { get; set; } = DateTime.Now;
        public Dictionary<string, string> Args { get; set; } = new Dictionary<string, string>();

        private IPEndPoint ip = new IPEndPoint(IPAddress.Any, 0);
        [MemoryPackAllowSerialize]
        public IPEndPoint IP
        {
            get
            {
                if (Connection != null)
                {
                    ip = Connection.Address;
                }
                return ip;
            }
            set
            {
                ip = value;
            }
        }

        private bool connected = false;
        public bool Connected
        {
            get
            {
                if (Connection != null)
                {
                    connected = Connection.Connected == true;
                }
                return connected;
            }
            set
            {
                connected = value;
            }
        }

        [MemoryPackIgnore, JsonIgnore, BsonIgnore]
        public IConnection Connection { get; set; }

        [MemoryPackIgnore, JsonIgnore, BsonIgnore]
        public uint Order { get; set; } = int.MaxValue;
    }


    [MemoryPackable]
    public sealed partial class SignInfo
    {

        public string MachineId { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
        public string GroupId { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;

        public Dictionary<string, string> Args { get; set; } = new Dictionary<string, string>();

        [MemoryPackIgnore]
        [JsonIgnore]
        public IConnection Connection { get; set; }
    }
}
