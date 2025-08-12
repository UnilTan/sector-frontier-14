using Content.Client.Cargo.UI;
using Content.Shared._NF.Cargo.BUI;
using Content.Shared.Cargo.Events;
using Robust.Client.UserInterface;

namespace Content.Client._NF.Cargo.BUI;

// Client-side bound UI for the cargo pallet console.
// RU: Клиентская привязанная UI для консоли паллет.
// Uses the NF suffix to avoid collisions and manages the CargoPalletMenu window,
// RU: Суффикс NF избегает конфликтов; управляет окном CargoPalletMenu,
// forwarding user actions to the server via UI messages.
// RU: перенаправляет действия пользователя на сервер через UI-сообщения.
public sealed class CargoPalletConsoleNFBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private CargoPalletMenu? _menu;

    public CargoPalletConsoleNFBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    // Opens the window if needed and wires up button events.
    // RU: Открывает окно при необходимости и подписывает обработчики кнопок.
    protected override void Open()
    {
        base.Open();

        if (_menu == null)
        {
            _menu = this.CreateWindow<CargoPalletMenu>();
            _menu.AppraiseRequested += OnAppraisal;
            _menu.SellRequested += OnSell;
        }
    }

    // Sends an appraisal request to the server.
    // RU: Отправляет на сервер запрос на оценку (обновление предварительной стоимости).
    private void OnAppraisal()
    {
        SendMessage(new CargoPalletAppraiseMessage());
    }

    // Sends a sell request to the server.
    // RU: Отправляет на сервер запрос на продажу.
    private void OnSell()
    {
        SendMessage(new CargoPalletSellMessage());
    }

    // Receives state updates from the server and updates the window accordingly.
    // RU: Получает состояние от сервера и обновляет окно.
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not NFCargoPalletConsoleInterfaceState palletState)
            return;

        _menu?.SetEnabled(palletState.Enabled);
        _menu?.SetAppraisal(palletState.Appraisal);
        _menu?.SetReal(palletState.Real);
        _menu?.SetCount(palletState.Count);
        _menu?.SetReductionText(palletState.TotalReductionText ?? string.Empty);
        _menu?.SetMinimalUi(palletState.MinimalUi);
    }
}
