﻿using linker.libs;
using linker.libs.extends;
using LiteDB;
using LiteDB.Engine;
using System.Net;

namespace linker.store
{

    /// <summary>
    /// 持久化
    /// </summary>
    public sealed class Storefactory
    {
        LiteDatabase database;
        public Storefactory()
        {
            BsonMapper bsonMapper = new BsonMapper();
            bsonMapper.RegisterType<IPEndPoint>(serialize: (a) => a.ToString(), deserialize: (a) => IPEndPoint.Parse(a.AsString));
            bsonMapper.RegisterType<IPAddress>(serialize: (a) => a.ToString(), deserialize: (a) => IPAddress.Parse(a.AsString));
            bsonMapper.RegisterType<IPAddress[]>(serialize: (a) => a.ToJson(), deserialize: (a) => a.AsString.DeJson<IPAddress[]>());

            if (Encoded() == false)
            {
                database = new LiteDatabase(@"./configs/db.db", bsonMapper);
                var rebuildOptions = new RebuildOptions { Password = Helper.GlobalString };
                database.Rebuild(rebuildOptions);
            }
            else
            {
                database = new LiteDatabase(new ConnectionString($"Filename=./configs/db.db; Password={Helper.GlobalString}"), bsonMapper);
            }
        }

        private bool Encoded()
        {
            try
            {
                string text = File.ReadAllText("./configs/db.db");
                return text.Contains("This is a LiteDB file") == false;
            }
            catch (Exception)
            {
            }
            return false;
        }

        public ILiteCollection<T> GetCollection<T>(string name)
        {
            return database.GetCollection<T>(name);
        }

        public void Confirm()
        {
            database.Checkpoint();
        }
    }

}