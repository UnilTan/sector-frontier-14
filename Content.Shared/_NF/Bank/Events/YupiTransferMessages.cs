using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Content.Shared.CartridgeLoader;

namespace Content.Shared._NF.Bank.Events;

[Serializable, NetSerializable]
public sealed class YupiTransferRequestMessage : CartridgeMessageEvent
{
    public string TargetCode = string.Empty; // case-insensitive, must be exactly 6 chars A-Z/1-9
    public int Amount;
}

[Serializable, NetSerializable]
public sealed class YupiTransferErrorPopupMessage : BoundUserInterfaceState
{
    public readonly string Error;
    public YupiTransferErrorPopupMessage(string error)
    {
        Error = error;
    }
}


