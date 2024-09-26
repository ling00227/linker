﻿using linker.plugins.flow;

namespace linker.plugins.messenger
{
    public sealed class MessengerFlow : IFlow
    {
        public ulong ReceiveBytes { get; private set; }
        public ulong SendtBytes { get; private set; }
        public string FlowName => "Messenger";

        private Dictionary<ushort, FlowItemInfo> flows { get; } = new Dictionary<ushort, FlowItemInfo>();
        public MessengerFlow()
        {
            Add(ushort.MaxValue);
        }

        public void Add(ushort id)
        {
            flows.TryAdd(id, new FlowItemInfo());
        }

        public void AddReceive(ushort id, ulong bytes)
        {
            if (flows.TryGetValue(id, out FlowItemInfo messengerFlowItemInfo))
            {
                ReceiveBytes += bytes;
                messengerFlowItemInfo.ReceiveBytes += bytes;
            }
            else if (flows.TryGetValue(ushort.MaxValue, out messengerFlowItemInfo))
            {
                ReceiveBytes += bytes;
                messengerFlowItemInfo.ReceiveBytes += bytes;
            }
        }
        public void AddSendt(ushort id, ulong bytes)
        {
            if (flows.TryGetValue(id, out FlowItemInfo messengerFlowItemInfo))
            {
                SendtBytes += bytes;
                messengerFlowItemInfo.SendtBytes += bytes;
            }
            else if (flows.TryGetValue(ushort.MaxValue, out messengerFlowItemInfo))
            {
                SendtBytes += bytes;
                messengerFlowItemInfo.SendtBytes += bytes;
            }
        }

        public Dictionary<ushort, FlowItemInfo> GetFlows()
        {
            return flows;
        }
    }
}
