using AutoCustomerDhcp;
using Il2Cpp;

internal static class Program
{
	private static int _passed;
	private static int _failed;

	private static int Main()
	{
		Run("Mudar cliente aplica IP do cliente selecionado", () =>
		{
			(NetworkMap map, Server server) = Setup("10.11.0.2", 101, 1);
			map.Customers[11] = Customer(101, 1, "10.11.0.2", "10.11.0.3");
			server.CustomerId = 22;
			map.Customers[22] = Customer(202, 1, "10.22.0.2", "10.22.0.3");
			string message = CustomerDhcpService.Assign(server);
			Equal("10.22.0.2", server.IP, "O IP deve pertencer ao novo cliente.");
			Equal(202, server.appID, "O aplicativo deve ser o do novo pool.");
			Equal(22, map.CustomerQueries.Single(), "A busca deve usar GetCustomerID atual.");
			AssertApplied(server, "10.22.0.2", 202);
			AssertMessage(message, "IP");
		});

		Run("IP atual livre no pool correto é preservado sem SetIP", () =>
		{
			(NetworkMap map, Server server) = Setup("10.22.0.3", 202, 1);
			map.Customers[22] = Customer(202, 1, "10.22.0.2", "10.22.0.3");
			string message = CustomerDhcpService.Assign(server);
			Equal("10.22.0.3", server.IP);
			Equal(0, server.SetIpCalls, "Não deve reaplicar IP que já é válido.");
			Equal(1, server.UpdateAppIdCalls, "O vínculo ao app deve ser atualizado.");
			Equal(0, server.DirectIpWrites);
			AssertMessage(message, "IP");
		});

		Run("Pools para outro tipo de servidor são ignorados", () =>
		{
			(NetworkMap map, Server server) = Setup("10.99.0.2", 100, 1);
			CustomerBase customer = Customer(101, 2, "10.22.0.2");
			customer.usableIpsPerApp[202] = new[] { "10.23.0.2", "10.23.0.3" };
			customer.appIdToServerType[202] = 1;
			map.Customers[22] = customer;
			CustomerDhcpService.Assign(server);
			AssertApplied(server, "10.23.0.2", 202);
		});

		Run("Sem IP livre preserva configuração sem SetIP ou UpdateAppID", () =>
		{
			(NetworkMap map, Server server) = Setup("10.99.0.2", 100, 1);
			map.Customers[22] = Customer(202, 1, "10.22.0.2", "10.22.0.3");
			map.Servers.Add(new Server("busy-1", 22, 1, 202, "10.22.0.2"));
			map.Servers.Add(new Server("busy-2", 22, 1, 202, "10.22.0.3"));
			string message = CustomerDhcpService.Assign(server);
			AssertUntouched(server, "10.99.0.2", 100);
			Assert(!string.IsNullOrWhiteSpace(message), "Falta mensagem de pool esgotado.");
			Assert(Contains(message, "livre") || Contains(message, "disponível"), "Mensagem deve explicar em português que falta IP livre.");
		});

		Run("Cliente ausente não altera configuração e explica motivo", () =>
		{
			(_, Server server) = Setup("10.99.0.2", 100, 1);
			string message = CustomerDhcpService.Assign(server);
			AssertUntouched(server, "10.99.0.2", 100);
			AssertMessage(message, "cliente");
		});

		Run("Ausência de pool compatível não altera configuração", () =>
		{
			(NetworkMap map, Server server) = Setup("10.99.0.2", 100, 1);
			map.Customers[22] = Customer(202, 2, "10.22.0.2");
			string message = CustomerDhcpService.Assign(server);
			AssertUntouched(server, "10.99.0.2", 100);
			Assert(!string.IsNullOrWhiteSpace(message), "Falta motivo para pool incompatível.");
		});

		Run("IP duplicado no pool é pulado", () =>
		{
			(NetworkMap map, Server server) = Setup("10.99.0.2", 100, 1);
			map.Customers[22] = Customer(202, 1, "10.22.0.2", "10.22.0.3");
			map.Servers.Add(new Server("busy", 22, 1, 202, "10.22.0.2"));
			CustomerDhcpService.Assign(server);
			AssertApplied(server, "10.22.0.3", 202);
		});

		Run("Checagem de duplicidade exclui o próprio servidor", () =>
		{
			(NetworkMap map, Server server) = Setup("10.22.0.2", 202, 1);
			map.Customers[22] = Customer(202, 1, "10.22.0.2", "10.22.0.3");
			CustomerDhcpService.Assign(server);
			Equal("10.22.0.2", server.IP);
			Equal(0, server.SetIpCalls);
			Assert(map.DuplicateQueries.Count > 0, "Deve consultar duplicidade nativa.");
			Assert(map.DuplicateQueries.All(query => ReferenceEquals(query.ServerToExclude, server)), "Todas as consultas devem passar o servidor atual como exclusão.");
			Equal(0, server.DirectIpWrites);
		});

		Run("Gateway reservado pela validação nativa é pulado", () =>
		{
			(NetworkMap map, Server server) = Setup("10.99.0.2", 100, 1);
			map.Customers[22] = Customer(202, 1, "10.22.0.1", "10.22.0.2");
			map.ReservedIps.Add("10.22.0.1");
			CustomerDhcpService.Assign(server);
			AssertApplied(server, "10.22.0.2", 202);
			Assert(map.DuplicateQueries.Any(query => query.Ip == "10.22.0.1"), "Gateway deve ser vetado pela validação do jogo.");
		});

		Run("IP atual reservado não é preservado", () =>
		{
			(NetworkMap map, Server server) = Setup("10.22.0.1", 202, 1);
			map.Customers[22] = Customer(202, 1, "10.22.0.1", "10.22.0.2");
			map.ReservedIps.Add("10.22.0.1");
			CustomerDhcpService.Assign(server);
			AssertApplied(server, "10.22.0.2", 202);
		});

		Run("Mudança de app usa UpdateAppID mesmo quando preserva IP", () =>
		{
			(NetworkMap map, Server server) = Setup("10.22.0.2", 100, 1);
			map.Customers[22] = Customer(202, 1, "10.22.0.2", "10.22.0.3");
			CustomerDhcpService.Assign(server);
			Equal("10.22.0.2", server.IP);
			Equal(0, server.SetIpCalls);
			Equal(202, server.appID);
			Equal(1, server.UpdateAppIdCalls);
			Equal(0, server.DirectIpWrites);
		});

		Console.WriteLine($"Runtime adapter: {_passed} passed, {_failed} failed.");
		return _failed == 0 ? 0 : 1;
	}

	private static (NetworkMap Map, Server Server) Setup(string ip, int appId, int type)
	{
		var map = new NetworkMap();
		var server = new Server("selected", 22, type, appId, ip);
		map.Servers.Add(server);
		NetworkMap.instance = map;
		return (map, server);
	}

	private static CustomerBase Customer(int appId, int type, params string[] addresses)
	{
		return new CustomerBase
		{
			usableIpsPerApp = new Dictionary<int, string[]> { [appId] = addresses },
			appIdToServerType = new Dictionary<int, int> { [appId] = type }
		};
	}

	private static void AssertApplied(Server server, string ip, int appId)
	{
		Equal(ip, server.IP);
		Equal(appId, server.appID);
		Equal(1, server.SetIpCalls, "IP novo exige SetIP exatamente uma vez.");
		Equal(1, server.UpdateAppIdCalls, "App selecionado exige UpdateAppID.");
		Equal(0, server.DirectIpWrites, "Adapter não pode escrever Server.IP diretamente.");
		Equal("SetIP:" + ip, server.Calls[0], "SetIP deve preceder atualização de app.");
		Equal("UpdateAppID:" + appId, server.Calls[1]);
	}

	private static void AssertUntouched(Server server, string ip, int appId)
	{
		Equal(ip, server.IP);
		Equal(appId, server.appID);
		Equal(0, server.SetIpCalls);
		Equal(0, server.UpdateAppIdCalls);
		Equal(0, server.DirectIpWrites);
	}

	private static void AssertMessage(string message, string word)
	{
		Assert(!string.IsNullOrWhiteSpace(message) && Contains(message, word), "Mensagem deve informar '" + word + "': " + message);
	}

	private static bool Contains(string value, string word) => value.Contains(word, StringComparison.OrdinalIgnoreCase);

	private static void Equal<T>(T expected, T actual, string? description = null)
	{
		Assert(EqualityComparer<T>.Default.Equals(expected, actual), (description ?? "Valores diferentes.") + $" Expected: {expected}; actual: {actual}.");
	}

	private static void Assert(bool condition, string description = "Assertion failed.")
	{
		if (!condition) throw new InvalidOperationException(description);
	}

	private static void Run(string name, Action test)
	{
		try
		{
			test();
			_passed++;
			Console.WriteLine("PASS " + name);
		}
		catch (Exception exception)
		{
			_failed++;
			Console.WriteLine("FAIL " + name + ": " + exception.Message);
		}
	}
}
