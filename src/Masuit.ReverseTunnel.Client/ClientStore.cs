using Microsoft.Data.Sqlite;

namespace Masuit.ReverseTunnel.Client;

/// <summary>使用本地 SQLite 保存多条内网隧道配置。</summary>
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

	public IReadOnlyList<ClientConfiguration> LoadAll()
	{
		lock (_gate)
		{
			using var connection = OpenConnection();
			using var command = connection.CreateCommand();
			command.CommandText = "SELECT Id, Name, ServerUrl, TunnelId, Token, LocalHost, LocalPort, Enabled FROM TunnelConfigurations ORDER BY Id";
			using SqliteDataReader reader = command.ExecuteReader();
			var configurations = new List<ClientConfiguration>();
			while (reader.Read())
			{
				configurations.Add(ReadConfiguration(reader));
			}
			return configurations;
		}
	}

	public ClientConfiguration? Load(long id)
	{
		lock (_gate)
		{
			using var connection = OpenConnection();
			using var command = connection.CreateCommand();
			command.CommandText = "SELECT Id, Name, ServerUrl, TunnelId, Token, LocalHost, LocalPort, Enabled FROM TunnelConfigurations WHERE Id = $id";
			command.Parameters.AddWithValue("$id", id);
			using SqliteDataReader reader = command.ExecuteReader();
			return reader.Read() ? ReadConfiguration(reader) : null;
		}
	}

	public ClientConfiguration Save(ClientConfiguration configuration)
	{
		lock (_gate)
		{
			using var connection = OpenConnection();
			using var command = connection.CreateCommand();
			command.CommandText = configuration.Id > 0
				? """
					UPDATE TunnelConfigurations
					SET Name = $name, ServerUrl = $serverUrl, TunnelId = $tunnelId, Token = $token, LocalHost = $localHost, LocalPort = $localPort, Enabled = $enabled, UpdatedAt = $updatedAt
					WHERE Id = $id;
					"""
				: """
					INSERT INTO TunnelConfigurations (Name, ServerUrl, TunnelId, Token, LocalHost, LocalPort, Enabled, UpdatedAt)
					VALUES ($name, $serverUrl, $tunnelId, $token, $localHost, $localPort, $enabled, $updatedAt);
					""";
			command.Parameters.AddWithValue("$id", configuration.Id);
			command.Parameters.AddWithValue("$name", configuration.Name.Trim());
			command.Parameters.AddWithValue("$serverUrl", configuration.ServerUrl.Trim());
			command.Parameters.AddWithValue("$tunnelId", configuration.TunnelId.Trim());
			command.Parameters.AddWithValue("$token", configuration.Token.Trim());
			command.Parameters.AddWithValue("$localHost", configuration.LocalHost.Trim());
			command.Parameters.AddWithValue("$localPort", configuration.LocalPort);
			command.Parameters.AddWithValue("$enabled", configuration.Enabled);
			command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
			command.ExecuteNonQuery();

			if (configuration.Id > 0)
			{
				return Load(configuration.Id) ?? throw new InvalidOperationException("保存隧道配置后无法读取记录。");
			}
			using var idCommand = connection.CreateCommand();
			idCommand.CommandText = "SELECT last_insert_rowid()";
			long id = Convert.ToInt64(idCommand.ExecuteScalar());
			return Load(id) ?? throw new InvalidOperationException("创建隧道配置后无法读取记录。");
		}
	}

	public bool Delete(long id)
	{
		lock (_gate)
		{
			using var connection = OpenConnection();
			using var command = connection.CreateCommand();
			command.CommandText = "DELETE FROM TunnelConfigurations WHERE Id = $id";
			command.Parameters.AddWithValue("$id", id);
			return command.ExecuteNonQuery() > 0;
		}
	}

	public bool SetEnabled(long id, bool enabled)
	{
		lock (_gate)
		{
			using var connection = OpenConnection();
			using var command = connection.CreateCommand();
			command.CommandText = "UPDATE TunnelConfigurations SET Enabled = $enabled, UpdatedAt = $updatedAt WHERE Id = $id";
			command.Parameters.AddWithValue("$id", id);
			command.Parameters.AddWithValue("$enabled", enabled);
			command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
			return command.ExecuteNonQuery() > 0;
		}
	}

	public bool HasDuplicate(string serverUrl, string tunnelId, long exceptId = 0)
	{
		lock (_gate)
		{
			using var connection = OpenConnection();
			using var command = connection.CreateCommand();
			command.CommandText = "SELECT 1 FROM TunnelConfigurations WHERE lower(ServerUrl) = lower($serverUrl) AND lower(TunnelId) = lower($tunnelId) AND Id <> $exceptId LIMIT 1";
			command.Parameters.AddWithValue("$serverUrl", serverUrl.Trim());
			command.Parameters.AddWithValue("$tunnelId", tunnelId.Trim());
			command.Parameters.AddWithValue("$exceptId", exceptId);
			return command.ExecuteScalar() is not null;
		}
	}

	private void Initialize()
	{
		lock (_gate)
		{
			using var connection = OpenConnection();
			using var command = connection.CreateCommand();
			command.CommandText = """
				CREATE TABLE IF NOT EXISTS TunnelConfigurations (
					Id INTEGER PRIMARY KEY AUTOINCREMENT,
					Name TEXT NOT NULL DEFAULT '',
					ServerUrl TEXT NOT NULL DEFAULT '',
					TunnelId TEXT NOT NULL DEFAULT '',
					Token TEXT NOT NULL DEFAULT '',
					LocalHost TEXT NOT NULL DEFAULT '127.0.0.1',
					LocalPort INTEGER NOT NULL DEFAULT 80,
					Enabled INTEGER NOT NULL DEFAULT 0,
					UpdatedAt TEXT NOT NULL
				);
				CREATE UNIQUE INDEX IF NOT EXISTS IX_TunnelConfigurations_ServerUrl_TunnelId
					ON TunnelConfigurations (ServerUrl COLLATE NOCASE, TunnelId COLLATE NOCASE);
				CREATE TABLE IF NOT EXISTS ClientConfiguration (
					Id INTEGER PRIMARY KEY CHECK (Id = 1),
					ServerUrl TEXT NOT NULL DEFAULT '',
					TunnelId TEXT NOT NULL DEFAULT '',
					Token TEXT NOT NULL DEFAULT '',
					LocalHost TEXT NOT NULL DEFAULT '127.0.0.1',
					LocalPort INTEGER NOT NULL DEFAULT 80,
					Enabled INTEGER NOT NULL DEFAULT 0,
					UpdatedAt TEXT NOT NULL
				);
				INSERT INTO TunnelConfigurations (Name, ServerUrl, TunnelId, Token, LocalHost, LocalPort, Enabled, UpdatedAt)
				SELECT CASE WHEN trim(TunnelId) = '' THEN '默认隧道' ELSE trim(TunnelId) END, ServerUrl, TunnelId, Token, LocalHost, LocalPort, Enabled, UpdatedAt
				FROM ClientConfiguration
				WHERE Id = 1 AND NOT EXISTS (SELECT 1 FROM TunnelConfigurations);
				""";
			command.ExecuteNonQuery();
		}
	}

	private static ClientConfiguration ReadConfiguration(SqliteDataReader reader)
	{
		return new ClientConfiguration(
			reader.GetInt64(0),
			reader.GetString(1),
			reader.GetString(2),
			reader.GetString(3),
			reader.GetString(4),
			reader.GetString(5),
			reader.GetInt32(6),
			reader.GetBoolean(7));
	}

	private SqliteConnection OpenConnection()
	{
		var connection = new SqliteConnection(_connectionString);
		connection.Open();
		return connection;
	}
}

/// <summary>内网穿透客户端的持久化配置。</summary>
public sealed record ClientConfiguration(long Id, string Name, string ServerUrl, string TunnelId, string Token, string LocalHost, int LocalPort, bool Enabled)
{
	public static ClientConfiguration Empty { get; } = new(0, "", "", "", "", "127.0.0.1", 80, false);

	public string? Validate()
	{
		if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 128)
		{
			return "隧道名称不能为空且不能超过 128 个字符。";
		}
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

/// <summary>Web 管理端提交的隧道配置，密钥为空时表示沿用已有密钥。</summary>
public sealed record ClientConfigurationRequest(string Name, string ServerUrl, string TunnelId, string Token, string LocalHost, int LocalPort, bool Enabled);
