using Microsoft.Data.Sqlite;

namespace Masuit.ReverseTunnel.Client;

/// <summary>
/// 使用本地 SQLite 保存客户端配置。数据库目录通过 ReverseTunnelClient__DataDirectory 配置，默认是 /data。
/// </summary>
public sealed class ClientStore
{
	private readonly string _connectionString;
	private readonly object _gate = new();

	public ClientStore(IConfiguration configuration)
	{
		string dataDirectory = configuration["ReverseTunnelClient:DataDirectory"] ?? "/data";
		Directory.CreateDirectory(dataDirectory);
		string databasePath = Path.Combine(dataDirectory, "client.db");
		_connectionString = new SqliteConnectionStringBuilder
		{
			DataSource = databasePath,
			Mode = SqliteOpenMode.ReadWriteCreate,
			Cache = SqliteCacheMode.Shared
		}.ToString();
		Initialize();
	}

	public ClientConfiguration Load()
	{
		lock (_gate)
		{
			using var connection = OpenConnection();
			using var command = connection.CreateCommand();
			command.CommandText = "SELECT ServerUrl, TunnelId, Token, LocalHost, LocalPort, Enabled FROM ClientConfiguration WHERE Id = 1";
			using SqliteDataReader reader = command.ExecuteReader();
			if (!reader.Read())
			{
				return ClientConfiguration.Empty;
			}
			return new ClientConfiguration(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetBoolean(5));
		}
	}

	public ClientConfiguration LoadForWeb()
	{
		return Load() with { Token = "" };
	}

	public void Save(ClientConfiguration configuration)
	{
		lock (_gate)
		{
			using var connection = OpenConnection();
			using var command = connection.CreateCommand();
			command.CommandText = """
				INSERT INTO ClientConfiguration (Id, ServerUrl, TunnelId, Token, LocalHost, LocalPort, Enabled, UpdatedAt)
				VALUES (1, $serverUrl, $tunnelId, $token, $localHost, $localPort, $enabled, $updatedAt)
				ON CONFLICT(Id) DO UPDATE SET ServerUrl = excluded.ServerUrl, TunnelId = excluded.TunnelId, Token = excluded.Token, LocalHost = excluded.LocalHost, LocalPort = excluded.LocalPort, Enabled = excluded.Enabled, UpdatedAt = excluded.UpdatedAt;
				""";
			command.Parameters.AddWithValue("$serverUrl", configuration.ServerUrl.Trim());
			command.Parameters.AddWithValue("$tunnelId", configuration.TunnelId.Trim());
			command.Parameters.AddWithValue("$token", configuration.Token.Trim());
			command.Parameters.AddWithValue("$localHost", configuration.LocalHost.Trim());
			command.Parameters.AddWithValue("$localPort", configuration.LocalPort);
			command.Parameters.AddWithValue("$enabled", configuration.Enabled);
			command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
			command.ExecuteNonQuery();
		}
	}

	private void Initialize()
	{
		lock (_gate)
		{
			using var connection = OpenConnection();
			using var command = connection.CreateCommand();
			command.CommandText = """
				CREATE TABLE IF NOT EXISTS ClientConfiguration (
					Id INTEGER PRIMARY KEY CHECK (Id = 1), ServerUrl TEXT NOT NULL DEFAULT '', TunnelId TEXT NOT NULL DEFAULT '', Token TEXT NOT NULL DEFAULT '', LocalHost TEXT NOT NULL DEFAULT '127.0.0.1', LocalPort INTEGER NOT NULL DEFAULT 80, Enabled INTEGER NOT NULL DEFAULT 0, UpdatedAt TEXT NOT NULL
				);
				""";
			command.ExecuteNonQuery();
		}
	}

	private SqliteConnection OpenConnection()
	{
		var connection = new SqliteConnection(_connectionString);
		connection.Open();
		return connection;
	}
}

/// <summary>内网穿透客户端的持久化配置。</summary>
public sealed record ClientConfiguration(string ServerUrl, string TunnelId, string Token, string LocalHost, int LocalPort, bool Enabled)
{
	public static ClientConfiguration Empty { get; } = new("", "", "", "127.0.0.1", 80, false);

	public string? Validate()
	{
		if (!Uri.TryCreate(ServerUrl.Trim(), UriKind.Absolute, out Uri? serverUri) || !new[] { "http", "https", "ws", "wss" }.Contains(serverUri.Scheme, StringComparer.OrdinalIgnoreCase))
		{
			return "服务端地址必须是 http、https、ws 或 wss 地址。";
		}
		if (string.IsNullOrWhiteSpace(TunnelId) || TunnelId.Length > 64 || !System.Text.RegularExpressions.Regex.IsMatch(TunnelId, "^[A-Za-z0-9_-]+$"))
		{
			return "隧道编号只能包含字母、数字、连字符和下划线，长度不超过 64。";
		}
		if (Token.Trim().Length < 16 || Token.Trim().Length > 256)
		{
			return "密钥长度必须在 16-256 个字符之间。";
		}
		if (string.IsNullOrWhiteSpace(LocalHost) || LocalPort is < 1 or > 65535)
		{
			return "本地目标主机不能为空，端口必须在 1-65535 之间。";
		}
		return null;
	}
}
