using System;
using System.IO;
using System.Threading.Tasks;
using MySqlConnector;
using Dapper;
using System.Net;
using CounterStrikeSharp.API;
using Newtonsoft.Json;
using MaxMind.Db; 
using MaxMind.GeoIP2;
using System.Collections.Generic; 
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Capabilities;
using LevelsRanks.API;

namespace LevelsRanksModuleExStatsGeoIP;

[MinimumApiVersion(80)]
public class LevelsRanksModuleExStatsGeoIP : BasePlugin
{
    public override string ModuleName => "[LR] ExStats GeoIP";
    public override string ModuleVersion => "1.0";
    public override string ModuleAuthor => "ABKAM designed by RoadSide Romeo & Wend4r";
    public override string ModuleDescription => "A plugin for GeoIP information.";
    
    private readonly PluginCapability<IPointsManager> _pointsManagerCapability = new("levelsranks");
    private IPointsManager? _pointsManager;

    
    public override void OnAllPluginsLoaded(bool hotReload)
    {
        base.Load(hotReload);
        
        _pointsManager = _pointsManagerCapability.Get();
        
        if (_pointsManager == null)
        {
            Server.PrintToConsole("Points management system is currently unavailable.");
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
            var steamId = ConvertSteamID64ToSteamID(steamId64);
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
            var countryName = cityResponse.Country.Names["en"];
            var regionName = cityResponse.MostSpecificSubdivision.Names["en"];
            var cityName = cityResponse.City.Names["en"];
            var countryCode = cityResponse.Country.IsoCode;

            var connectionString = _pointsManager.GetConnectionString();
            var dbConfig = _pointsManager.GetDatabaseConfig();
            using (var connection = new MySqlConnection(connectionString))
            {
                await connection.OpenAsync();
                var query = $@"
                INSERT INTO `{dbConfig.Name}_geoip` (steam, clientip, country, region, city, country_code)
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

                Console.WriteLine($"[GeoIP] Player {steamId} with IP {playerIp} and location {countryName}, {regionName}, {cityName} logged.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GeoIP] Error logging player connection for {steamId} with IP {playerIp}: {ex.Message}");
        }
    }
    private const string CreateTableQuery = @"CREATE TABLE IF NOT EXISTS `{0}_geoip` (
    `steam` varchar(32) NOT NULL,
    `clientip` varchar(16) NOT NULL,
    `country` varchar(48) NOT NULL,
    `region` varchar(48) NOT NULL,
    `city` varchar(48) NOT NULL,
    `country_code` varchar(4) NOT NULL,
    PRIMARY KEY (`steam`)
) CHARSET=utf8;";
    private void CreateDbTableIfNotExists()
    {
        var connectionString = _pointsManager.GetConnectionString();
        var dbConfig = _pointsManager.GetDatabaseConfig();
        using var connection = new MySqlConnection(connectionString);
        connection.Open();
        
        var tableName = $"{dbConfig.Name}";
        var query = string.Format(CreateTableQuery, tableName);
    
        connection.Execute(query);
    }

    public static string ConvertSteamID64ToSteamID(string steamId64)
    {
        if (ulong.TryParse(steamId64, out var communityId) && communityId > 76561197960265728)
        {
            var authServer = (communityId - 76561197960265728) % 2;
            var authId = (communityId - 76561197960265728 - authServer) / 2;
            return $"STEAM_1:{authServer}:{authId}";
        }
        return null; 
    }    
}