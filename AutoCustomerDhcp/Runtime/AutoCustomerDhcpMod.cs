using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(AutoCustomerDhcp.AutoCustomerDhcpMod), "Auto Customer DHCP", "0.1.0", "Lucas SNP")]
[assembly: MelonGame("Waseku", "Data Center")]

namespace AutoCustomerDhcp;

public sealed class AutoCustomerDhcpMod : MelonMod
{
    private sealed record PendingSelection(Server Server, int CustomerId, int Frame);

    private static readonly Dictionary<int, PendingSelection> Pending = new();
    private static string _message = "";
    private static float _messageUntil;

    public override void OnInitializeMelon()
    {
        var target = AccessTools.Method(typeof(Server), nameof(Server.ButtonClickChangeCustomer), new[] { typeof(bool) });
        if (target == null)
            throw new MissingMethodException("Seleção nativa de cliente não encontrada nesta versão do jogo.");

        HarmonyInstance.Patch(target,
            postfix: new HarmonyMethod(typeof(AutoCustomerDhcpMod), nameof(OnCustomerSelected)));
        MelonLogger.Msg("Auto Customer DHCP 0.1.0: IP automático ao escolher o cliente pelas setas do servidor.");
    }

    // Only the user's selection button schedules DHCP. Save restoration and other
    // mods calling UpdateCustomer do not trigger an unsolicited reassignment.
    private static void OnCustomerSelected(Server __instance)
    {
        try
        {
            if (__instance != null)
                Pending[__instance.GetInstanceID()] = new PendingSelection(__instance, __instance.GetCustomerID(), Time.frameCount);
        }
        catch (Exception exception)
        {
            MelonLogger.Warning("DHCP: não foi possível ler a seleção do cliente: " + exception.Message);
        }
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        Pending.Clear();
        _message = "";
    }

    public override void OnUpdate()
    {
        if (Pending.Count == 0)
            return;

        var networkSystem = WaypointInitializationSystem.Instance;
        if (networkSystem != null && networkSystem.isLoading)
            return;

        foreach (var entry in Pending.ToArray())
        {
            var selection = entry.Value;
            if (Time.frameCount <= selection.Frame)
                continue;

            Pending.Remove(entry.Key);
            try
            {
                var server = selection.Server;
                if (server == null || server.GetCustomerID() != selection.CustomerId)
                    continue;

                ShowMessage(CustomerDhcpService.Assign(server));
            }
            catch (Exception exception)
            {
                MelonLogger.Error("DHCP: falha ao configurar o servidor: " + exception);
                ShowMessage("DHCP: não foi possível configurar o IP; consulte o log do MelonLoader.");
            }
        }
    }

    private static void ShowMessage(string message)
    {
        _message = message;
        _messageUntil = Time.realtimeSinceStartup + 6f;
        MelonLogger.Msg(message);
    }

    public override void OnGUI()
    {
        if (_message.Length == 0 || Time.realtimeSinceStartup >= _messageUntil)
            return;

        var width = Math.Min(760f, Screen.width - 24f);
        GUI.Box(new Rect((Screen.width - width) / 2f, Screen.height - 92f, width, 64f), _message);
    }
}
