using Content.Server.CartridgeLoader;
using Content.Server.Popups;
using Content.Server._NF.Bank;
using Content.Shared._NF.Bank.BUI;
using Content.Shared._NF.Bank.Events;
using Content.Shared.CartridgeLoader;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Preferences;
using Robust.Shared.Player;
using Content.Server.Preferences.Managers;
using Robust.Server.Containers;
using System.Threading.Tasks;

namespace Content.Server._NF.CartridgeLoader.Cartridges;

[RegisterComponent]
public sealed partial class YupiTransferCartridgeComponent : Component {}

public sealed class YupiTransferCartridgeSystem : EntitySystem
{
	[Dependency] private readonly CartridgeLoaderSystem _cartridgeLoader = default!;
	[Dependency] private readonly BankSystem _bank = default!;
	[Dependency] private readonly PopupSystem _popup = default!;
	[Dependency] private readonly ContainerSystem _container = default!;

	public override void Initialize()
	{
		base.Initialize();
		SubscribeLocalEvent<YupiTransferCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
		SubscribeLocalEvent<YupiTransferCartridgeComponent, CartridgeMessageEvent>(OnUiMessage);
	}

	private void OnUiReady(Entity<YupiTransferCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
	{
		var loader = args.Loader;
		var code = "";
		var balance = 0;

		var owner = GetRootOwner(loader);
		var playerMan = IoCManager.Resolve<ISharedPlayerManager>();
		var prefsMan = IoCManager.Resolve<IServerPreferencesManager>();

		if (playerMan.TryGetSessionByEntity(owner, out var session))
		{
			_bank.TryGetBalance(session, out balance);
			if (prefsMan.TryGetCachedPreferences(session.UserId, out var prefs) &&
				prefs.SelectedCharacter is HumanoidCharacterProfile profile)
				code = profile.YupiAccountCode;

			// Fire-and-forget: ensure and push updated state
			_ = EnsureAndPushAsync(loader, session);
		}
		else
		{
			_bank.TryGetBalance(loader, out balance);
		}

		_cartridgeLoader.UpdateCartridgeUiState(loader, new YupiTransferUiState(code, balance));
	}

	private void OnUiMessage(Entity<YupiTransferCartridgeComponent> ent, ref CartridgeMessageEvent args)
	{
		var loader = GetEntity(args.LoaderUid);
		if (args is not YupiTransferRequestMessage msg)
			return;

		if (_bank.TryYupiTransfer(loader, msg.TargetCode, msg.Amount, out var error, out var newBal, out var recvAmount, out var recvCode))
		{
			_cartridgeLoader.UpdateCartridgeUiState(loader, new YupiTransferUiState(GetCode(loader), newBal));
			// Outgoing transfer popup to sender (only sender sees it)
			var owner = GetRootOwner(loader);
			_popup.PopupEntity(Loc.GetString("yupi-outgoing-transfer", ("code", GetCode(loader)), ("amount", recvAmount)), owner, owner);
			if (_bank.TryResolveOnlineByYupiCode(msg.TargetCode, out var target, out _))
				// Incoming popup visible only to the receiver
				_popup.PopupEntity(Loc.GetString("yupi-incoming-transfer", ("code", GetCode(loader)), ("amount", recvAmount)), target, target);
			return;
		}

		var errText = error switch
		{
			BankSystem.YupiTransferError.InvalidTarget => Loc.GetString("yupi-error-invalid-target"),
			BankSystem.YupiTransferError.SelfTransfer => Loc.GetString("yupi-error-self-transfer"),
			BankSystem.YupiTransferError.InvalidAmount => Loc.GetString("yupi-error-invalid-amount"),
			BankSystem.YupiTransferError.ExceedsPerTransferLimit => Loc.GetString("yupi-error-over-50k"),
			BankSystem.YupiTransferError.InsufficientFunds => Loc.GetString("bank-insufficient-funds"),
			BankSystem.YupiTransferError.ExceedsWindowLimit => Loc.GetString("yupi-error-window-limit"),
			_ => Loc.GetString("bank-atm-menu-transaction-denied")
		};
		_cartridgeLoader.UpdateCartridgeUiState(loader, new YupiTransferUiState(GetCode(loader), GetBalance(loader)));
		// Error shown only to the sender
		_popup.PopupEntity(errText, GetRootOwner(loader), GetRootOwner(loader));
	}

	private string GetCode(EntityUid loader)
	{
		var playerMan = IoCManager.Resolve<ISharedPlayerManager>();
		var prefsMan = IoCManager.Resolve<IServerPreferencesManager>();
		var owner = GetRootOwner(loader);
		if (playerMan.TryGetSessionByEntity(owner, out var session) &&
			prefsMan.TryGetCachedPreferences(session.UserId, out var prefs) &&
			prefs.SelectedCharacter is HumanoidCharacterProfile profile)
			return profile.YupiAccountCode;
		return string.Empty;
	}

	private int GetBalance(EntityUid loader)
	{
		var playerMan = IoCManager.Resolve<ISharedPlayerManager>();
		var owner = GetRootOwner(loader);
		if (playerMan.TryGetSessionByEntity(owner, out var session))
		{
			_bank.TryGetBalance(session, out var bal);
			return bal;
		}
		_bank.TryGetBalance(loader, out var fb);
		return fb;
	}

	private EntityUid GetRootOwner(EntityUid ent)
	{
		var current = ent;
		while (_container.TryGetContainingContainer(current, out var cont))
		{
			current = cont.Owner;
		}
		return current;
	}

	private async Task EnsureAndPushAsync(EntityUid loader, ICommonSession session)
	{
		var ensured = await _bank.EnsureYupiForSessionSelected(session);
		if (string.IsNullOrEmpty(ensured))
			return;
		_bank.TryGetBalance(session, out var bal);
		_cartridgeLoader.UpdateCartridgeUiState(loader, new YupiTransferUiState(ensured, bal));
	}
}
