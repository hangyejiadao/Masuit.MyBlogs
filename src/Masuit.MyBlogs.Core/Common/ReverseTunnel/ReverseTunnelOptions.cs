using System.Net;

namespace Masuit.MyBlogs.Core.Common.ReverseTunnel;

/// <summary>
/// 反向隧道服务端的安全配置。
/// </summary>
public sealed class ReverseTunnelOptions
{
	/// <summary>
	/// 允许建立控制连接和数据连接的客户端 IP 或 CIDR 列表；为空表示不额外限制来源地址。
	/// </summary>
	public string[] AllowedClientIps { get; set; } = [];

	/// <summary>
	/// 判断指定来源地址是否命中白名单。
	/// </summary>
	/// <param name="address">请求的实际远端 IP。</param>
	/// <returns>允许访问时为 true。</returns>
	public bool IsAllowed(IPAddress address)
	{
		string[] allowedClientIps = AllowedClientIps ?? [];
		if (allowedClientIps.Length == 0)
		{
			return true;
		}
		foreach (string item in allowedClientIps)
		{
			if (string.IsNullOrWhiteSpace(item))
			{
				continue;
			}
			if (TryParseCidr(item, out IPAddress network, out int prefixLength) && IsInCidr(address, network, prefixLength))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// 解析单个 IP 或 CIDR 文本。
	/// </summary>
	/// <param name="value">IP 或 CIDR 文本。</param>
	/// <param name="network">网络地址。</param>
	/// <param name="prefixLength">网络前缀长度。</param>
	/// <returns>格式正确时为 true。</returns>
	private static bool TryParseCidr(string value, out IPAddress network, out int prefixLength)
	{
		network = IPAddress.None;
		prefixLength = 0;
		string[] parts = value.Trim().Split('/', 2, StringSplitOptions.TrimEntries);
		if (!IPAddress.TryParse(parts[0], out network))
		{
			return false;
		}
		if (network.IsIPv4MappedToIPv6)
		{
			network = network.MapToIPv4();
		}
		int maxPrefixLength = network.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
		prefixLength = maxPrefixLength;
		return parts.Length == 1 || int.TryParse(parts[1], out prefixLength) && prefixLength >= 0 && prefixLength <= maxPrefixLength;
	}

	/// <summary>
	/// 判断地址是否属于指定网络。
	/// </summary>
	/// <param name="address">待判断地址。</param>
	/// <param name="network">网络地址。</param>
	/// <param name="prefixLength">网络前缀长度。</param>
	/// <returns>属于该网络时为 true。</returns>
	private static bool IsInCidr(IPAddress address, IPAddress network, int prefixLength)
	{
		if (address.IsIPv4MappedToIPv6)
		{
			address = address.MapToIPv4();
		}
		if (network.IsIPv4MappedToIPv6)
		{
			network = network.MapToIPv4();
		}
		byte[] addressBytes = address.GetAddressBytes();
		byte[] networkBytes = network.GetAddressBytes();
		if (addressBytes.Length != networkBytes.Length)
		{
			return false;
		}
		int fullBytes = prefixLength / 8;
		int remainingBits = prefixLength % 8;
		for (int index = 0; index < fullBytes; index++)
		{
			if (addressBytes[index] != networkBytes[index])
			{
				return false;
			}
		}
		return remainingBits == 0 || (addressBytes[fullBytes] & (byte)(0xFF << (8 - remainingBits))) == (networkBytes[fullBytes] & (byte)(0xFF << (8 - remainingBits)));
	}
}
