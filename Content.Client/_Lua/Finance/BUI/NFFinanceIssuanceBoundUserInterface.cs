/*
 * LuaWorld - This file is licensed under AGPLv3
 * Copyright (c) 2025 LuaWorld Contributors
 * See AGPLv3.txt for details.
 */

using Content.Shared.UserInterface;
using Content.Shared._NF.Finance.BUI;
using Content.Shared._NF.Finance.Events;
using Robust.Client.UserInterface;
using Content.Client._Lua.Finance.UI; //Lua

namespace Content.Client._NF.Finance.BUI;

public sealed class NFFinanceIssuanceBoundUserInterface : BoundUserInterface
{
    public NFFinanceIssuanceBoundUserInterface(EntityUid owner, Enum key) : base(owner, key)
    {
    }

    private NFFinanceIssuanceWindow? _window;

    protected override void Open()
    {
        base.Open();
        if (_window != null)
        {
            _window.MoveToFront();
        }
        else
        {
            _window = new NFFinanceIssuanceWindow();
            _window.IssueRequested += amt => SendMessage(new FinanceIssueLoanRequestMessage(amt));
            _window.OnClose += () => { _window = null; };
            _window.OpenCentered();
        }
        // запросим рейтинг для заполнения
        SendMessage(new FinanceRatingQueryMessage(""));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;
        if (_window != null)
        {
            _window.Close();
            _window = null;
        }
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (_window == null)
            return;
        switch (state)
        {
            case FinanceRatingState r:
                _window.UpdateRating(r);
                break;
            case FinanceIssueLoanResponseState res:
                _window.UpdateResult(res);
                if (res.Success)
                {
                    // Автообновление рейтинга после успешной выдачи
                    SendMessage(new FinanceRatingQueryMessage(""));
                }
                break;
        }
    }
}


