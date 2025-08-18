using Robust.Shared.Serialization;

namespace Content.Shared._NF.Bank.BUI;

[Serializable, NetSerializable]
public sealed class YupiTransferUiState : BoundUserInterfaceState
{
    public readonly string OwnCode;
    public readonly int Balance;

    public YupiTransferUiState(string ownCode, int balance)
    {
        OwnCode = ownCode;
        Balance = balance;
    }
}


