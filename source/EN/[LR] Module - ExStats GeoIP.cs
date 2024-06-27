using System;
using System.IO;
using System.Threading.Tasks;
using MySqlConnector;
using Dapper;
using System.Net;
using CounterStrikeSharp.API;
using MaxMind.GeoIP2;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Capabilities;
using LevelsRanksApi;

namespace LevelsRanksModuleExStatsGeoIP;

[MinimumApiVersion(80)]
public class LevelsRanksModuleExStatsGeoIP : BasePlugin
{
    public override string ModuleName => "[LR] Module - ExStats GeoIP";
    public override string ModuleVersion => "1.1.0";
    public override string ModuleAuthor => "ABKAM designed by RoadSide Romeo & Wend4r";
    public override string ModuleDescription => "A plugin for GeoIP information.";
    
    private readonly PluginCapability<ILevelsRanksApi> _levelsRanksApiCapability = new("levels_ranks");
    private ILevelsRanksApi? _levelsRanksApi;

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        base.Load(hotReload);
        
        _levelsRanksApi = _levelsRanksApiCapability.Get();
        
        if (_levelsRanksApi == null)
        {
            Server.PrintToConsole("LevelsRanks API is currently unavailable.");
            return;
        }

        RegisterListener<Listeners.OnClientConnected>(OnClientConnected);
        CreateDbTableIfNotExists();
    }

    private void OnClientConnected(int playerSlot)
    {
        var player = Utilities.GetPlayerFromSlot(playerSlot);
        if (player != null && !player.IsBot)
        {
            var steamId64 = player.SteamID.ToString();
            var steamId = _levelsRanksApi.ConvertToSteamId(ulong.Parse(steamId64));
            var playerIp = player.IpAddress?.Split(':')[0]; 

            if (!string.IsNullOrEmpty(playerIp))
            {
                LogPlayerConnectionAsync(steamId, playerIp).ConfigureAwait(false);
            }
        } 
    }
    private async Task LogPlayerConnectionAsync(string steamId, string playerIp)
    {
        try
        {
            using var reader = new DatabaseReader(Path.Combine(ModuleDirectory, "GeoLite2-City.mmdb"));
            var ipAddress = IPAddress.Parse(playerIp);
            var cityResponse = reader.City(ipAddress);

            var countryName = GetNameFromResponse(cityResponse.Country.Names, "country");
            var regionName = GetNameFromResponse(cityResponse.MostSpecificSubdivision.Names, "region");
            var cityName = GetNameFromResponse(cityResponse.City.Names, "city");
            var countryCode = cityResponse.Country.IsoCode ?? "N/A";

            var connectionString = _levelsRanksApi.DbConnectionString;
            var tableName = $"{_levelsRanksApi.TableName}_geoip";
            using (var connection = new MySqlConnection(connectionString))
            {
                await connection.OpenAsync();
                var query = $@"
                INSERT INTO `{tableName}` (steam, clientip, country, region, city, country_code)
                VALUES (@SteamID, @ClientIP, @Country, @Region, @City, @CountryCode)
                ON DUPLICATE KEY UPDATE 
                    clientip = @ClientIP, 
                    country = @Country, 
                    region = @Region, 
                    city = @City, 
                    country_code = @CountryCode;";

                await connection.ExecuteAsync(query, new 
                {
                    SteamID = steamId, 
                    ClientIP = playerIp, 
                    Country = countryName, 
                    Region = regionName, 
                    City = cityName, 
                    CountryCode = countryCode
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GeoIP] Error logging player connection for {steamId} with IP {playerIp}: {ex.Message}");
        }
    }

    private string GetNameFromResponse(IReadOnlyDictionary<string, string> names, string entity)
    {
        if (names == null) return "N/A";
        if (names.ContainsKey("en"))
            return names["en"];
        if (names.ContainsKey("ru"))
            return names["ru"];
        return $"Unknown {entity}";
    }

    private const string CreateTableQuery = @"CREATE TABLE IF NOT EXISTS `{0}` (
        `steam` varchar(32) NOT NULL,
        `clientip` varchar(16) NOT NULL,
        `country` varchar(48) NOT NULL,
        `region` varchar(48) NOT NULL,
        `city` varchar(48) NOT NULL,
        `country_code` varchar(4) NOT NULL,
        PRIMARY KEY (`steam`)
    ) CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;";

    private void CreateDbTableIfNotExists()
    {
        var connectionString = _levelsRanksApi.DbConnectionString;
        var tableName = $"{_levelsRanksApi.TableName}_geoip";
        using var connection = new MySqlConnection(connectionString);
        connection.Open();
        
        var query = string.Format(CreateTableQuery, tableName);
    
        connection.Execute(query);
    }
}
