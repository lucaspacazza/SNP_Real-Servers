namespace Il2Cpp;

// These fakes exercise the adapter without loading Unity or the game's DLLs.
public interface INetworkEndpoint
{
	string IP { get; }
	string ServerID { get; }
}

public sealed class Server : INetworkEndpoint
{
	private string _ip;

	public Server(string serverId, int customerId, int type, int appId, string initialIp)
	{
		ServerID = serverId;
		CustomerId = customerId;
		serverType = type;
		appID = appId;
		_ip = initialIp;
	}

	public string ServerID { get; set; }
	public int CustomerId { get; set; }
	public int serverType;
	public int appID;
	public int SetIpCalls { get; private set; }
	public int UpdateAppIdCalls { get; private set; }
	public int DirectIpWrites { get; private set; }
	public List<string> Calls { get; } = new();

	public string IP
	{
		get => _ip;
		set
		{
			DirectIpWrites++;
			_ip = value;
		}
	}

	public int GetCustomerID() => CustomerId;

	public void SetIP(string ip)
	{
		SetIpCalls++;
		Calls.Add("SetIP:" + ip);
		_ip = ip;
	}

	public void UpdateAppID(int appId)
	{
		UpdateAppIdCalls++;
		Calls.Add("UpdateAppID:" + appId);
		appID = appId;
	}
}

public sealed class CustomerBase
{
	public Dictionary<int, string[]> usableIpsPerApp = new();
	public Dictionary<int, int> appIdToServerType = new();
}

public sealed class NetworkMap
{
	public static NetworkMap? instance;
	public Dictionary<int, CustomerBase> Customers { get; } = new();
	public List<INetworkEndpoint> Servers { get; } = new();
	public Dictionary<string, INetworkEndpoint> servers => Servers.ToDictionary(endpoint => endpoint.ServerID);
	public HashSet<string> ReservedIps { get; } = new(StringComparer.Ordinal);
	public List<(string Ip, Server ServerToExclude)> DuplicateQueries { get; } = new();
	public List<int> CustomerQueries { get; } = new();

	public CustomerBase? GetCustomerBase(int customerId)
	{
		CustomerQueries.Add(customerId);
		return Customers.TryGetValue(customerId, out CustomerBase? customer) ? customer : null;
	}

	public IEnumerable<INetworkEndpoint> GetAllServers() => Servers;

	public bool IsIpAddressDuplicate(string ip, Server serverToExclude)
	{
		DuplicateQueries.Add((ip, serverToExclude));
		return ReservedIps.Contains(ip)
			|| Servers.Any(endpoint => endpoint.ServerID != serverToExclude.ServerID && endpoint.IP == ip);
	}
}
