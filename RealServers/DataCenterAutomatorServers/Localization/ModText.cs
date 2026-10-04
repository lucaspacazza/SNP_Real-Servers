using System;
using System.Collections.Generic;

namespace DataCenterAutomatorServers;

// Mirrors the game's Localisation.Languages enum (1..19). Product names remain
// unchanged; only the UI added by this mod is translated.
internal static class ModText
{
	private static readonly Dictionary<string, string[]> Text = new Dictionary<string, string[]>
	{
		["Close"] = new[]
		{
			"Close", "Zavrit", "Fermer", "Chiudi", "Schliessen", "Cerrar", "\u0625\u063a\u0644\u0627\u0642", "Sluiten", "\u9589\u3058\u308b", "\ub2eb\uae30", "Fechar", "Fechar", "\u0417\u0430\u043a\u0440\u044b\u0442\u044c", "\u5173\u95ed", "Cerrar", "Kapat", "Zamknij", "\u0e1b\u0e34\u0e14", "\u95dc\u9589"
		},
		["MountedServers"] = new[]
		{
			"Mounted servers", "Nainstalovane servery", "Serveurs installes", "Server installati", "Installierte Server", "Servidores montados", "\u0627\u0644\u062e\u0648\u0627\u062f\u0645 \u0627\u0644\u0645\u062b\u0628\u062a\u0629", "Gemonteerde servers", "\u642d\u8f09\u6e08\u307f\u30b5\u30fc\u30d0\u30fc", "\uc7a5\ucc29\ub41c \uc11c\ubc84", "Servidores montados", "Servidores montados", "\u0423\u0441\u0442\u0430\u043d\u043e\u0432\u043b\u0435\u043d\u043d\u044b\u0435 \u0441\u0435\u0440\u0432\u0435\u0440\u044b", "\u5df2\u5b89\u88c5\u7684\u670d\u52a1\u5668", "Servidores montados", "Takili sunucular", "Zamontowane serwery", "\u0e40\u0e0b\u0e34\u0e23\u0e4c\u0e1f\u0e40\u0e27\u0e2d\u0e23\u0e4c\u0e17\u0e35\u0e48\u0e15\u0e34\u0e14\u0e15\u0e31\u0e49\u0e07", "\u5df2\u5b89\u88dd\u7684\u4f3a\u670d\u5668"
		},
		["Servers"] = new[]
		{
			"Servers", "Servery", "Serveurs", "Server", "Server", "Servidores", "\u062e\u0648\u0627\u062f\u0645", "Servers", "\u30b5\u30fc\u30d0\u30fc", "\uc11c\ubc84", "Servidores", "Servidores", "\u0421\u0435\u0440\u0432\u0435\u0440\u044b", "\u670d\u52a1\u5668", "Servidores", "Sunucular", "Serwery", "\u0e40\u0e0b\u0e34\u0e23\u0e4c\u0e1f\u0e40\u0e27\u0e2d\u0e23\u0e4c", "\u4f3a\u670d\u5668"
		},
		["Configure"] = new[]
		{
			"Configure", "Konfigurovat", "Configurer", "Configura", "Konfigurieren", "Configurar", "\u0625\u0639\u062f\u0627\u062f", "Configureren", "\u69cb\u6210", "\uad6c\uc131", "Configurar", "Configurar", "\u041d\u0430\u0441\u0442\u0440\u043e\u0438\u0442\u044c", "\u914d\u7f6e", "Configurar", "Yapilandir", "Konfiguruj", "\u0e15\u0e31\u0e49\u0e07\u0e04\u0e48\u0e32", "\u8a2d\u5b9a"
		},
		["PoweredOn"] = new[]
		{
			"On", "Zapnuto", "Allume", "Acceso", "Ein", "Encendido", "\u0642\u064a\u062f \u0627\u0644\u062a\u0634\u063a\u064a\u0644", "Aan", "\u30aa\u30f3", "\ucf1c\uc9d0", "Ligado", "Ligado", "\u0412\u043a\u043b.", "\u5f00", "Encendido", "Acik", "Wlaczony", "\u0e40\u0e1b\u0e34\u0e14", "\u958b"
		},
		["PoweredOff"] = new[]
		{
			"Off", "Vypnuto", "Eteint", "Spento", "Aus", "Apagado", "\u0625\u064a\u0642\u0627\u0641 \u0627\u0644\u062a\u0634\u063a\u064a\u0644", "Uit", "\u30aa\u30d5", "\uc804\uc6d0 \ub044\uae40", "Desligado", "Desligado", "\u0412\u044b\u043a\u043b.", "\u5173", "Apagado", "Kapali", "Wylaczony", "\u0e1b\u0e34\u0e14", "\u95dc"
		},
		["ConfigurationSubtitle"] = new[]
		{
			"Customer and network configuration", "Konfigurace zakaznika a site", "Configuration client et reseau", "Configurazione cliente e rete", "Kunden- und Netzwerkkonfiguration", "Configuracion de cliente y red", "\u0625\u0639\u062f\u0627\u062f \u0627\u0644\u0639\u0645\u064a\u0644 \u0648\u0627\u0644\u0634\u0628\u0643\u0629", "Klant- en netwerkconfiguratie", "\u9867\u5ba2\u3068\u30cd\u30c3\u30c8\u30ef\u30fc\u30af\u306e\u69cb\u6210", "\uace0\uac1d \ubc0f \ub124\ud2b8\uc6cc\ud06c \uad6c\uc131", "Configuracao de cliente e rede", "Configuracao de cliente e rede", "\u041d\u0430\u0441\u0442\u0440\u043e\u0439\u043a\u0430 \u043a\u043b\u0438\u0435\u043d\u0442\u0430 \u0438 \u0441\u0435\u0442\u0438", "\u5ba2\u6237\u548c\u7f51\u7edc\u914d\u7f6e", "Configuracion de cliente y red", "Musteri ve ag yapilandirmasi", "Konfiguracja klienta i sieci", "\u0e01\u0e32\u0e23\u0e15\u0e31\u0e49\u0e07\u0e04\u0e48\u0e32\u0e25\u0e39\u0e01\u0e04\u0e49\u0e32\u0e41\u0e25\u0e30\u0e40\u0e04\u0e23\u0e37\u0e2d\u0e02\u0e48\u0e32\u0e22", "\u5ba2\u6236\u8207\u7db2\u8def\u8a2d\u5b9a"
		},
		["Customer"] = new[]
		{
			"Customer", "Zakaznik", "Client", "Cliente", "Kunde", "Cliente", "\u0627\u0644\u0639\u0645\u064a\u0644", "Klant", "\u9867\u5ba2", "\uace0\uac1d", "Cliente", "Cliente", "\u041a\u043b\u0438\u0435\u043d\u0442", "\u5ba2\u6237", "Cliente", "Musteri", "Klient", "\u0e25\u0e39\u0e01\u0e04\u0e49\u0e32", "\u5ba2\u6236"
		},
		["NoCustomer"] = new[]
		{
			"No customer selected", "Neni vybran zakaznik", "Aucun client selectionne", "Nessun cliente selezionato", "Kein Kunde ausgewahlt", "Ningun cliente seleccionado", "\u0644\u0645 \u064a\u062a\u0645 \u062a\u062d\u062f\u064a\u062f \u0639\u0645\u064a\u0644", "Geen klant geselecteerd", "\u9867\u5ba2\u304c\u9078\u629e\u3055\u308c\u3066\u3044\u307e\u305b\u3093", "\uc120\ud0dd\ub41c \uace0\uac1d\uc774 \uc5c6\uc2b5\ub2c8\ub2e4", "Nenhum cliente selecionado", "Nenhum cliente selecionado", "\u041a\u043b\u0438\u0435\u043d\u0442 \u043d\u0435 \u0432\u044b\u0431\u0440\u0430\u043d", "\u672a\u9009\u62e9\u5ba2\u6237", "Ningun cliente seleccionado", "Musteri secilmedi", "Nie wybrano klienta", "\u0e22\u0e31\u0e07\u0e44\u0e21\u0e48\u0e44\u0e14\u0e49\u0e40\u0e25\u0e37\u0e2d\u0e01\u0e25\u0e39\u0e01\u0e04\u0e49\u0e32", "\u672a\u9078\u64c7\u5ba2\u6236"
		},
		["Network"] = new[]
		{
			"Network", "Sit", "Reseau", "Rete", "Netzwerk", "Red", "\u0627\u0644\u0634\u0628\u0643\u0629", "Netwerk", "\u30cd\u30c3\u30c8\u30ef\u30fc\u30af", "\ub124\ud2b8\uc6cc\ud06c", "Rede", "Rede", "\u0421\u0435\u0442\u044c", "\u7f51\u7edc", "Red", "Ag", "Siec", "\u0e40\u0e04\u0e23\u0e37\u0e2d\u0e02\u0e48\u0e32\u0e22", "\u7db2\u8def"
		},
		["CurrentIp"] = new[]
		{
			"Current IP address", "Aktualni IP adresa", "Adresse IP actuelle", "Indirizzo IP attuale", "Aktuelle IP-Adresse", "Direccion IP actual", "\u0639\u0646\u0648\u0627\u0646 IP \u0627\u0644\u062d\u0627\u0644\u064a", "Huidig IP-adres", "\u73fe\u5728\u306eIP\u30a2\u30c9\u30ec\u30b9", "\ud604\uc7ac IP \uc8fc\uc18c", "Endereco IP atual", "Endereco IP atual", "\u0422\u0435\u043a\u0443\u0449\u0438\u0439 IP-\u0430\u0434\u0440\u0435\u0441", "\u5f53\u524d IP \u5730\u5740", "Direccion IP actual", "Guncel IP adresi", "Biezacy adres IP", "\u0e17\u0e35\u0e48\u0e2d\u0e22\u0e39\u0e48 IP \u0e1b\u0e31\u0e08\u0e08\u0e38\u0e1a\u0e31\u0e19", "\u76ee\u524d IP \u4f4d\u5740"
		},
		["CustomerIpHint"] = new[]
		{
			"Select a customer above, then open the game's IP editor.", "Vyberte zakaznika a otevrete editor IP hry.", "Selectionnez un client puis ouvrez l'editeur IP du jeu.", "Seleziona un cliente e apri l'editor IP del gioco.", "Wahlen Sie einen Kunden und offnen Sie den IP-Editor des Spiels.", "Elige un cliente y abre el editor IP del juego.", "\u062d\u062f\u062f \u0639\u0645\u064a\u0644\u0627 \u062b\u0645 \u0627\u0641\u062a\u062d \u0645\u062d\u0631\u0631 IP \u0641\u064a \u0627\u0644\u0644\u0639\u0628\u0629.", "Kies een klant en open daarna de IP-editor van het spel.", "\u9867\u5ba2\u3092\u9078\u3093\u3067\u304b\u3089\u30b2\u30fc\u30e0\u306eIP\u30a8\u30c7\u30a3\u30bf\u30fc\u3092\u958b\u3044\u3066\u304f\u3060\u3055\u3044\u3002", "\uace0\uac1d\uc744 \uc120\ud0dd\ud55c \ud6c4 \uac8c\uc784 IP \ud3b8\uc9d1\uae30\ub97c \uc5f4\uc5b4 \uc8fc\uc138\uc694.", "Escolha um cliente e abra o editor de IP do jogo.", "Escolha um cliente e abra o editor de IP do jogo.", "\u0412\u044b\u0431\u0435\u0440\u0438\u0442\u0435 \u043a\u043b\u0438\u0435\u043d\u0442\u0430 \u0438 \u043e\u0442\u043a\u0440\u043e\u0439\u0442\u0435 IP-\u0440\u0435\u0434\u0430\u043a\u0442\u043e\u0440 \u0438\u0433\u0440\u044b.", "\u9009\u62e9\u5ba2\u6237\u540e\u6253\u5f00\u6e38\u620f IP \u7f16\u8f91\u5668\u3002", "Elige un cliente y abre el editor IP del juego.", "Bir musteri secin ve oyunun IP duzenleyicisini acin.", "Wybierz klienta, a nastepnie otworz edytor IP gry.", "\u0e40\u0e25\u0e37\u0e2d\u0e01\u0e25\u0e39\u0e01\u0e04\u0e49\u0e32\u0e41\u0e25\u0e49\u0e27\u0e40\u0e1b\u0e34\u0e14\u0e15\u0e31\u0e27\u0e41\u0e01\u0e49\u0e44\u0e02 IP \u0e02\u0e2d\u0e07\u0e40\u0e01\u0e21.", "\u9078\u64c7\u5ba2\u6236\u5f8c\u958b\u555f\u904a\u6232\u7684 IP \u7de8\u8f2f\u5668\u3002"
		},
		["ConfigureIp"] = new[]
		{
			"Configure IP", "Nastavit IP", "Configurer IP", "Configura IP", "IP konfigurieren", "Configurar IP", "\u0625\u0639\u062f\u0627\u062f IP", "IP configureren", "IP\u3092\u69cb\u6210", "IP \uad6c\uc131", "Configurar IP", "Configurar IP", "\u041d\u0430\u0441\u0442\u0440\u043e\u0438\u0442\u044c IP", "\u914d\u7f6e IP", "Configurar IP", "IP yapilandir", "Konfiguruj IP", "\u0e15\u0e31\u0e49\u0e07\u0e04\u0e48\u0e32 IP", "\u8a2d\u5b9a IP"
		},
		["ServerStatus"] = new[] { "Server status", "Stav serveru", "Etat du serveur", "Stato del server", "Serverstatus", "Estado del servidor", "\u062d\u0627\u0644\u0629 \u0627\u0644\u062e\u0627\u062f\u0645", "Serverstatus", "\u30b5\u30fc\u30d0\u30fc\u306e\u72b6\u614b", "\uc11c\ubc84 \uc0c1\ud0dc", "Estado do servidor", "Estado do servidor", "\u0421\u043e\u0441\u0442\u043e\u044f\u043d\u0438\u0435 \u0441\u0435\u0440\u0432\u0435\u0440\u0430", "\u670d\u52a1\u5668\u72b6\u6001", "Estado del servidor", "Sunucu durumu", "Stan serwera", "\u0e2a\u0e16\u0e32\u0e19\u0e30\u0e40\u0e0b\u0e34\u0e23\u0e4c\u0e1f\u0e40\u0e27\u0e2d\u0e23\u0e4c", "\u4f3a\u670d\u5668\u72c0\u614b" },
		["IpAddress"] = new[] { "IP address", "IP adresa", "Adresse IP", "Indirizzo IP", "IP-Adresse", "Direccion IP", "\u0639\u0646\u0648\u0627\u0646 IP", "IP-adres", "IP\u30a2\u30c9\u30ec\u30b9", "IP \uc8fc\uc18c", "Endereco IP", "Endereco IP", "IP-\u0430\u0434\u0440\u0435\u0441", "IP \u5730\u5740", "Direccion IP", "IP adresi", "Adres IP", "\u0e17\u0e35\u0e48\u0e2d\u0e22\u0e39\u0e48 IP", "IP \u4f4d\u5740" },
		["Configuration"] = new[] { "Configuration", "Konfigurace", "Configuration", "Configurazione", "Konfiguration", "Configuracion", "\u0627\u0644\u0625\u0639\u062f\u0627\u062f", "Configuratie", "\u69cb\u6210", "\uad6c\uc131", "Configuracao", "Configuracao", "\u041d\u0430\u0441\u0442\u0440\u043e\u0439\u043a\u0430", "\u914d\u7f6e", "Configuracion", "Yapilandirma", "Konfiguracja", "\u0e01\u0e32\u0e23\u0e15\u0e31\u0e49\u0e07\u0e04\u0e48\u0e32", "\u8a2d\u5b9a" },
		["PowerOn"] = new[] { "Turn on", "Zapnout", "Allumer", "Accendi", "Einschalten", "Encender", "\u062a\u0634\u063a\u064a\u0644", "Inschakelen", "\u96fb\u6e90\u3092\u5165\u308c\u308b", "\uc804\uc6d0 \ucf1c\uae30", "Ligar", "Ligar", "\u0412\u043a\u043b\u044e\u0447\u0438\u0442\u044c", "\u5f00\u673a", "Encender", "Ac", "Wlacz", "\u0e40\u0e1b\u0e34\u0e14", "\u958b\u6a5f" },
		["PowerOff"] = new[] { "Turn off", "Vypnout", "Eteindre", "Spegni", "Ausschalten", "Apagar", "\u0625\u064a\u0642\u0627\u0641", "Uitschakelen", "\u96fb\u6e90\u3092\u5207\u308b", "\uc804\uc6d0 \ub044\uae30", "Desligar", "Desligar", "\u0412\u044b\u043a\u043b\u044e\u0447\u0438\u0442\u044c", "\u5173\u673a", "Apagar", "Kapat", "Wylacz", "\u0e1b\u0e34\u0e14", "\u95dc\u6a5f" },
		["NoMounted"] = new[]
		{
			"No Real Servers device is mounted in a rack.", "Zadne zarizeni Real Servers neni namontovano v racku.", "Aucun appareil Real Servers n'est monte dans un rack.", "Nessun dispositivo Real Servers e montato in un rack.", "Kein Real-Servers-Gerat ist in einem Rack installiert.", "Ningun dispositivo de Real Servers esta montado en un rack.", "\u0644\u0627 \u064a\u0648\u062c\u062f \u062c\u0647\u0627\u0632 Real Servers \u0645\u062b\u0628\u062a \u0641\u064a \u062d\u0627\u0645\u0644.", "Geen Real Servers-apparaat is in een rack gemonteerd.", "Real Servers \u30c7\u30d0\u30a4\u30b9\u306f\u30e9\u30c3\u30af\u306b\u642d\u8f09\u3055\u308c\u3066\u3044\u307e\u305b\u3093\u3002", "\ub7a9\uc5d0 \uc7a5\ucc29\ub41c Real Servers \uc7a5\uce58\uac00 \uc5c6\uc2b5\ub2c8\ub2e4.", "Nenhum dispositivo do Real Servers esta montado em rack.", "Nenhum dispositivo do Real Servers esta montado em rack.", "\u0412 \u0441\u0442\u043e\u0439\u043a\u0443 \u043d\u0435 \u0443\u0441\u0442\u0430\u043d\u043e\u0432\u043b\u0435\u043d\u043e \u043d\u0438 \u043e\u0434\u043d\u043e\u0433\u043e \u0443\u0441\u0442\u0440\u043e\u0439\u0441\u0442\u0432\u0430 Real Servers.", "\u6ca1\u6709\u5df2\u5b89\u88c5\u5728\u673a\u67dc\u4e2d\u7684 Real Servers \u8bbe\u5907\u3002", "Ningun dispositivo de Real Servers esta montado en un rack.", "Rafa takili Real Servers cihazi yok.", "Zadne urzadzenie Real Servers nie jest zamontowane w szafie rack.", "\u0e44\u0e21\u0e48\u0e21\u0e35\u0e2d\u0e38\u0e1b\u0e01\u0e23\u0e13\u0e4c Real Servers \u0e17\u0e35\u0e48\u0e15\u0e34\u0e14\u0e15\u0e31\u0e49\u0e07\u0e43\u0e19\u0e41\u0e23\u0e47\u0e01", "\u6c92\u6709\u5df2\u5b89\u88dd\u5728\u6a5f\u67b6\u4e2d\u7684 Real Servers \u88dd\u7f6e\u3002"
		}
	};

	internal static string Get(string key)
	{
		if (!Text.TryGetValue(key, out string[] values))
		{
			return key;
		}
		int index = LanguageIndex();
		return index >= 0 && index < values.Length ? values[index] : values[0];
	}

	private static int LanguageIndex()
	{
		try
		{
			Type type = ReflectionUtil.FindType("Localisation");
			object instance = type == null ? null : ReflectionUtil.ReadStaticMember(type, "instance", "Instance");
			object language = instance == null ? null : ReflectionUtil.ReadMember(instance, "currentlySelectedLanguage", "CurrentlySelectedLanguage");
			int value = language == null ? 1 : Convert.ToInt32(language);
			return value - 1;
		}
		catch
		{
			return 0;
		}
	}
}
