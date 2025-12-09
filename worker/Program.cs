using System;
using System.Data.Common;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Newtonsoft.Json;
using Npgsql;
using StackExchange.Redis;

namespace Worker
{
    public class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                // --- Configuration du Réseau (Utilise les noms de services Docker) ---
                var dbHost = Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost";
                var redisHost = Environment.GetEnvironmentVariable("REDIS_HOST") ?? "localhost";
                
                // Préparation de la chaîne de connexion DB (pour connexion initiale et reconnexions)
                // L'application utilisera "db" comme nom d'hôte si DB_HOST est bien configuré.
                var dbConnectionString = $"Server={dbHost};Username=postgres;Password=postgres;";

                // --- Connexions initiales ---
                var pgsql = OpenDbConnection(dbConnectionString);
                var redisConn = OpenRedisConnection(redisHost);
                var redis = redisConn.GetDatabase();

                var keepAliveCommand = pgsql.CreateCommand();
                keepAliveCommand.CommandText = "SELECT 1";

                var definition = new { vote = "", voter_id = "" };
                
                Console.WriteLine("Worker is running and listening for votes...");

                while (true)
                {
                    // Délai de 100ms pour éviter de surcharger Redis/la CPU
                    Thread.Sleep(100); 

                    // --- Gestion de la reconnexion à Redis ---
                    if (redisConn == null || !redisConn.IsConnected) {
                        Console.WriteLine("Reconnecting Redis");
                        redisConn = OpenRedisConnection(redisHost);
                        redis = redisConn.GetDatabase();
                    }
                    
                    // Récupère un vote de la file d'attente Redis (List Left Pop)
                    string json = redis.ListLeftPopAsync("votes").Result; 
                    
                    if (json != null)
                    {
                        var vote = JsonConvert.DeserializeAnonymousType(json, definition);
                        Console.WriteLine($"Processing vote for '{vote.vote}' by '{vote.voter_id}'");

                        // --- Gestion de la reconnexion à PostgreSQL ---
                        if (!pgsql.State.Equals(System.Data.ConnectionState.Open))
                        {
                            Console.WriteLine("Reconnecting DB");
                            pgsql = OpenDbConnection(dbConnectionString);
                        }
                        else
                        {
                            // Sauvegarde le vote dans PostgreSQL (avec UPSERT)
                            UpdateVote(pgsql, vote.voter_id, vote.vote); 
                        }
                    }
                    else
                    {
                        // Si la file d'attente est vide, envoie un "keep-alive" à la DB
                        keepAliveCommand.ExecuteNonQuery(); 
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static NpgsqlConnection OpenDbConnection(string connectionString)
        {
            NpgsqlConnection connection;

            while (true)
            {
                try
                {
                    connection = new NpgsqlConnection(connectionString);
                    connection.Open();
                    break;
                }
                catch (SocketException)
                {
                    Console.Error.WriteLine("Waiting for db");
                    Thread.Sleep(1000);
                }
                catch (DbException)
                {
                    Console.Error.WriteLine("Waiting for db");
                    Thread.Sleep(1000);
                }
            }

            Console.Error.WriteLine("Connected to db");

            // Création de la table 'votes' si elle n'existe pas
            var command = connection.CreateCommand();
            command.CommandText = @"CREATE TABLE IF NOT EXISTS votes (
                                        id VARCHAR(255) NOT NULL UNIQUE,
                                        vote VARCHAR(255) NOT NULL
                                    )";
            command.ExecuteNonQuery();

            return connection;
        }

        private static ConnectionMultiplexer OpenRedisConnection(string hostname)
        {
            // Résout le nom d'hôte Docker en IP pour une meilleure compatibilité avec StackExchange.Redis
            var ipAddress = GetIp(hostname);
            Console.WriteLine($"Found redis at {ipAddress}");

            while (true)
            {
                try
                {
                    Console.Error.WriteLine("Connecting to redis");
                    return ConnectionMultiplexer.Connect(ipAddress);
                }
                catch (RedisConnectionException)
                {
                    Console.Error.WriteLine("Waiting for redis");
                    Thread.Sleep(1000);
                }
            }
        }

        private static string GetIp(string hostname)
            => Dns.GetHostEntryAsync(hostname)
                .Result
                .AddressList
                .First(a => a.AddressFamily == AddressFamily.InterNetwork)
                .ToString();

        private static void UpdateVote(NpgsqlConnection connection, string voterId, string vote)
        {
            var command = connection.CreateCommand();
            try
            {
                // Tente d'insérer (premier vote)
                command.CommandText = "INSERT INTO votes (id, vote) VALUES (@id, @vote)";
                command.Parameters.AddWithValue("@id", voterId);
                command.Parameters.AddWithValue("@vote", vote);
                command.ExecuteNonQuery();
            }
            catch (DbException)
            {
                // Si l'insertion échoue (l'ID existe déjà), met à jour le vote (re-vote)
                command.CommandText = "UPDATE votes SET vote = @vote WHERE id = @id";
                command.Parameters.Clear(); // Nettoie les anciens paramètres
                command.Parameters.AddWithValue("@id", voterId);
                command.Parameters.AddWithValue("@vote", vote);
                command.ExecuteNonQuery();
            }
            finally
            {
                command.Dispose();
            }
        }
    }
}