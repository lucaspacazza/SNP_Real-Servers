using System;
using MelonLoader;

namespace DataCenterAutomatorServers;

internal static class Log
{
	public static void Info(string message)
	{
		MelonLogger.Msg(message);
	}

	public static void Warning(string message)
	{
		MelonLogger.Warning(message);
	}

	public static void Error(string message)
	{
		MelonLogger.Error(message);
	}

	public static void Error(string message, Exception exception)
	{
		MelonLogger.Error($"{message} {exception}");
	}
}
