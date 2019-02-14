﻿
using System;
using System.Configuration;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Fetcho.Common.entities;
using Npgsql;
using log4net;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;
using System.Data.Common;
using Fetcho.Common.Entities;
using System.Collections.Generic;

namespace Fetcho.Common
{
    /// <summary>
    /// Connect to postgres
    /// </summary>
    public class Database : IDisposable
    {
        public const int ConnectionPoolWaitTimeInMilliseconds = 120000;
        private const int WaitTimeVarianceInMilliseconds = 250;

        static readonly BinaryFormatter formatter = new BinaryFormatter();
        public const int DefaultDatabasePort = 5432;
        public const int MaxConcurrentConnections = 10;
        static readonly ILog log = LogManager.GetLogger(typeof(Database));
        static readonly Random random = new Random(DateTime.Now.Millisecond);

        static readonly SemaphoreSlim connPool = new SemaphoreSlim(MaxConcurrentConnections);

        NpgsqlConnection conn;
        NpgsqlConnectionStringBuilder connstr;

        public string Server { get; set; }
        public int Port { get; set; }

        private bool IsOpen { get => (conn != null && conn.State == ConnectionState.Open); }

        public Database(string server, int port)
        {
            Server = server;
            Port = port;
        }

        public Database(string connString)
        {
            connstr = new NpgsqlConnectionStringBuilder(connString);
            if (!String.IsNullOrWhiteSpace(connstr.Host))
                Server = connstr.Host;
            if (connstr.Port > 0)
                Port = connstr.Port;
        }

        public Database() : this(ConfigurationManager.ConnectionStrings["db"]?.ToString())
        {
        }

        /// <summary>
        /// open a connection to the DB
        /// </summary>
        /// <returns></returns>
        async Task Open()
        {
            try
            {
                if (IsOpen) return;

                if (Port > 0)
                    connstr.Port = Port;
                if (!String.IsNullOrWhiteSpace(Server))
                    connstr.Host = Server;
                conn = new NpgsqlConnection(connstr.ToString());

                await WaitIfTooManyActiveConnections().ConfigureAwait(false);
                await conn.OpenAsync().ConfigureAwait(false);
                Server = conn.Host;
                Port = conn.Port;
            }
            catch (Exception ex)
            {
                log.Error("Open(): {0}", ex);
            }
        }

        /// <summary>
        /// Will block until a DB connection is available
        /// </summary>
        /// <returns></returns>
        async Task WaitIfTooManyActiveConnections()
        {
            DateTime start = DateTime.Now;
            while (!await connPool.WaitAsync(GetWaitTime()).ConfigureAwait(false))
                log.InfoFormat("Waiting for a database connection for {0}ms", (DateTime.Now - start).TotalMilliseconds);
        }

        /// <summary>
        /// Setup a base NpgsqlCommand
        /// </summary>
        /// <param name="commandtext"></param>
        /// <returns></returns>
        async Task<NpgsqlCommand> SetupCommand(string commandtext)
        {
            try
            {
                if (!IsOpen) await Open().ConfigureAwait(false);

                NpgsqlCommand cmd = new NpgsqlCommand(commandtext)
                {
                    Connection = conn,
                    CommandTimeout = 600000
                };

                return cmd;
            }
            catch (Exception ex)
            {
                log.Error("SetupCommand(): {0}", ex);
                return null;
            }
        }

        public async Task<Site> GetSite(Uri anyUri)
        {
            Site site = null;

            try
            {
                var hash = MD5Hash.Compute(anyUri.Host);

                NpgsqlCommand cmd = await SetupCommand("select hostname_hash, hostname, " +
                                                 "is_blocked, last_robots_fetched, robots_file " +
                                                 "from \"Site\" " +
                                                 "where hostname_hash = :hostname_hash"
                                                 );

                cmd.Parameters.AddWithValue("hostname_hash", hash.Values);


                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (reader.Read())
                    {
                        site = new Site
                        {
                            HostName = reader.GetString(1),
                            IsBlocked = reader.GetBoolean(2),
                            LastRobotsFetched = reader.GetFieldValue<DateTime?>(3),
                            RobotsFile = DeserializeField<RobotsFile>(reader, 4)
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error("GetSite(): {0}", ex);
            }

            return site;

        }

        public async Task BlockSite(string hostName)
        {
            NpgsqlCommand cmd = await SetupCommand("update \"Site\" set is_blocked = true " +
                                             "where hostname_hash = :hostname_hash");

            cmd.Parameters.AddWithValue("hostname_hash", MD5Hash.Compute(hostName).Values);

            var c = await cmd.ExecuteNonQueryAsync();
        }

        public async Task<int> SaveSite(Site site)
        {
            try
            {
                const string insert = "insert into \"Site\" (hostname_hash, hostname, is_blocked, last_robots_fetched, robots_file) " +
                  "values (:hostname_hash, :hostname, :is_blocked, :last_robots_fetched, :robots_file)";

                const string update = "update \"Site\" " +
                  "set    hostname = :hostname, " +
                  "       is_blocked = :is_blocked, " +
                  "       last_robots_fetched = :last_robots_fetched, " +
                  "       robots_file = :robots_file " +
                  "where  hostname_hash = :hostname_hash";

                NpgsqlCommand cmd = await SetupCommand(update);

                cmd.Parameters.AddWithValue("hostname_hash", site.Hash.Values);
                cmd.Parameters.AddWithValue("hostname", site.HostName);
                cmd.Parameters.AddWithValue("is_blocked", site.IsBlocked);

                SetBinaryParameter(cmd, "robots_file", site.RobotsFile);

                if (site.LastRobotsFetched.HasValue)
                    cmd.Parameters.Add(new NpgsqlParameter<DateTime>("last_robots_fetched", site.LastRobotsFetched.Value));
                else
                    cmd.Parameters.AddWithValue("last_robots_fetched", DBNull.Value);

                int count = await cmd.ExecuteNonQueryAsync();

                if (count == 0)
                {
                    cmd.CommandText = insert;
                    count = await cmd.ExecuteNonQueryAsync();
                }

                return count;
            }
            catch (Exception ex)
            {
                log.Error("SaveSite(): {0}", ex);
                return 0;
            }
        }

        public async Task SaveWebResource(Uri uri, DateTime nextFetch)
        {
            try
            {
                string updateSql = "set client_encoding='UTF8'; update \"WebResource\" " +
                  "set    next_fetch = :next_fetch " +
                  "where  urihash = :urihash;";

                string insertSql = "set client_encoding='UTF8'; insert into \"WebResource\" ( urihash, next_fetch ) " +
                  "values                      ( :urihash, :next_fetch );";

                NpgsqlCommand cmd = await SetupCommand(updateSql).ConfigureAwait(false);
                _saveWebResourceSetParams(cmd, uri, nextFetch);
                int count = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);

                if (count == 0) // no record to update
                {
                    cmd = await SetupCommand(insertSql).ConfigureAwait(false);
                    _saveWebResourceSetParams(cmd, uri, nextFetch);
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                log.ErrorFormat("SaveWebResource(): {0}", ex);
            }
        }

        static void _saveWebResourceSetParams(NpgsqlCommand cmd, Uri uri, DateTime nextfetch)
        {
            cmd.Parameters.Add(new NpgsqlParameter<byte[]>("urihash", MD5Hash.Compute(uri).Values));
            cmd.Parameters.Add(new NpgsqlParameter<DateTime>("next_fetch", nextfetch));
        }

        /// <summary>
        /// Returns true if the URI needs visiting
        /// </summary>
        /// <param name="uri"></param>
        /// <returns></returns>
        public async Task<bool> NeedsVisiting(Uri uri)
        {
            try
            {
                // the logic here looks backward, but it deals with the case where there's no records!
                NpgsqlCommand cmd = await SetupCommand("select count(urihash) from \"WebResource\" where urihash = :urihash and next_fetch > now();");

                cmd.Parameters.Add(new NpgsqlParameter("urihash", MD5Hash.Compute(uri).Values));

                object o = await cmd.ExecuteScalarAsync();

                return ((long)o) == 0;
            }
            catch (Exception ex)
            {
                log.ErrorFormat("NeedsVisiting(): {0}", ex);
                return true;
            }
        }

        /// <summary>
        /// Determines if an access key can access a specific workspace
        /// </summary>
        /// <param name="guid"></param>
        /// <returns></returns>
        public async Task<bool> HasWorkspaceAccess(Guid guid, string accesskey)
        {
            string sql =
                "select count(*) " +
                "from   \"WorkspaceAccessKeys\" " +
                "where  workspace_id = :workspace_id " +
                "       and access_key = :access_key;";

            NpgsqlCommand cmd = await SetupCommand(sql).ConfigureAwait(false);
            cmd.Parameters.Add(new NpgsqlParameter<Guid>("workspace_id", guid));
            cmd.Parameters.Add(new NpgsqlParameter<string>("access_key", accesskey));
            return (int)(await cmd.ExecuteScalarAsync().ConfigureAwait(false)) > 0;
        }

        /// <summary>
        /// Get a workspace record by it's GUID
        /// </summary>
        /// <param name="guid"></param>
        /// <returns></returns>
        public async Task<Workspace> GetWorkspace(Guid guid)
        {
            Workspace workspace = null;

            string sql =
                "select workspace_id, name, description, is_active, created " +
                "from   \"Workspace\" " +
                "where  workspace_id = :workspace_id;";

            NpgsqlCommand cmd = await SetupCommand(sql).ConfigureAwait(false);
            cmd.Parameters.Add(new NpgsqlParameter<Guid>("workspace_id", guid));

            // can't run this here until MARS is implemented on Npgsql
            // https://github.com/npgsql/npgsql/issues/462
            //var keys = GetWorkspaceAccessKeys(guid);

            using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
            {
                if (reader.Read())
                {
                    workspace = new Workspace
                    {
                        WorkspaceId = reader.GetGuid(0),
                        Name = reader.GetString(1),
                        Description = reader.IsDBNull(2) ? String.Empty : reader.GetFieldValue<string>(2),
                        IsActive = reader.GetBoolean(3),
                        Created = reader.GetDateTime(4)
                    };
                }
            }

            // see MARS comment
            //workspace.AccessKeys.AddRange(await keys);
            workspace.AccessKeys.AddRange(await GetWorkspaceAccessKeys(guid));

            return workspace;
        }

        public async Task SaveWorkspace(Workspace workspace)
        {
            string updateSql = "set client_encoding='UTF8'; update \"Workspace\" " +
              "set    name = :name, " +
              "       description = :description, " +
              "       is_active = :is_active " +
              "where  workspace_id = :workspace_id;";

            string insertSql = "set client_encoding='UTF8'; insert into \"Workspace\" ( workspace_id, name, description, is_active, created ) " +
              "values ( :workspace_id, :name, :description, :is_active, :created );";

            NpgsqlCommand cmd = await SetupCommand(updateSql).ConfigureAwait(false);
            _saveWorkspaceSetParams(cmd, workspace);
            int count = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);

            if (count == 0) // no record to update
            {
                cmd = await SetupCommand(insertSql).ConfigureAwait(false);
                _saveWorkspaceSetParams(cmd, workspace);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            await SaveWorkspaceAccessKeys(workspace);

        }

        /// <summary>
        /// Get a workspace record by it's GUID
        /// </summary>
        /// <param name="guid"></param>
        /// <returns></returns>
        public async Task DeleteWorkspace(Guid guid)
        {

            string sql =
                "delete  " +
                "from   \"Workspace\" " +
                "where  workspace_id = :workspace_id;";

            NpgsqlCommand cmd = await SetupCommand(sql).ConfigureAwait(false);
            cmd.Parameters.Add(new NpgsqlParameter<Guid>("workspace_id", guid));
            int count = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);

            if (count == 0)
                throw new Exception("No record was deleted");
        }

        private void _saveWorkspaceSetParams(NpgsqlCommand cmd, Workspace workspace)
        {
            cmd.Parameters.Add(new NpgsqlParameter<Guid>("workspace_id", workspace.WorkspaceId));
            cmd.Parameters.Add(new NpgsqlParameter<string>("name", workspace.Name));
            cmd.Parameters.Add(new NpgsqlParameter<string>("description", workspace.Description));
            cmd.Parameters.Add(new NpgsqlParameter<bool>("is_active", workspace.IsActive));
            cmd.Parameters.Add(new NpgsqlParameter<DateTime>("created", workspace.Created));
        }

        public async Task<IEnumerable<WorkspaceAccessKey>> GetWorkspaceAccessKeys(Guid workspaceId)
        {
            var l = new List<WorkspaceAccessKey>();


            string sql = "select workspace_id, access_key, is_active, is_owner, is_revoked, created, expiry " +
                "from \"WorkspaceAccessKeys\" " +
                "where workspace_id = :workspace_id;";

            NpgsqlCommand cmd = await SetupCommand(sql).ConfigureAwait(false);
            cmd.Parameters.Add(new NpgsqlParameter<Guid>("workspace_id", workspaceId));

            using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
            {
                while (reader.Read())
                {
                    l.Add(new WorkspaceAccessKey()
                    {
                        AccessKey = reader.GetString(1),
                        Created = reader.GetDateTime(5),
                        Expiry = reader.GetDateTime(6),
                        IsActive = reader.GetBoolean(2),
                        IsOwner = reader.GetBoolean(3),
                        IsRevoked = reader.GetBoolean(4)
                    });
                }
            }

            return l;
        }

        public async Task SaveWorkspaceAccessKeys(Workspace workspace)
        {
            foreach (var k in workspace.AccessKeys)
                await SaveWorkspaceAccessKey(workspace.WorkspaceId, k);
        }

        public async Task SaveWorkspaceAccessKey(Guid workspaceId, WorkspaceAccessKey wak)
        {
            string updateSql = "set client_encoding='UTF8'; update \"WorkspaceAccessKeys\" " +
              "set    is_active = :is_active, " +
              "       is_owner = :is_owner, " +
              "       is_revoked = :is_revoked, " +
              "       created = :created, " +
              "       expiry = :expiry " +
              "where  workspace_id = :workspace_id and access_key = :access_key;";

            string insertSql = "set client_encoding='UTF8'; insert into \"WorkspaceAccessKeys\" ( workspace_id, access_key, is_active, is_owner, is_revoked, created, expiry) " +
              "values ( :workspace_id, :access_key, :is_active, :is_owner, :is_revoked, :created, :expiry);";

            NpgsqlCommand cmd = await SetupCommand(updateSql).ConfigureAwait(false);
            _saveWorkspaceAccessKeysSetParams(cmd, workspaceId, wak);
            int count = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);

            if (count == 0) // no record to update
            {
                cmd = await SetupCommand(insertSql).ConfigureAwait(false);
                _saveWorkspaceAccessKeysSetParams(cmd, workspaceId, wak);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

        }

        private void _saveWorkspaceAccessKeysSetParams(NpgsqlCommand cmd, Guid workspaceId, WorkspaceAccessKey wak)
        {
            cmd.Parameters.Add(new NpgsqlParameter<Guid>("workspace_id", workspaceId));
            cmd.Parameters.Add(new NpgsqlParameter<string>("access_key", wak.AccessKey));
            cmd.Parameters.Add(new NpgsqlParameter<bool>("is_owner", wak.IsOwner));
            cmd.Parameters.Add(new NpgsqlParameter<bool>("is_active", wak.IsActive));
            cmd.Parameters.Add(new NpgsqlParameter<bool>("is_revoked", wak.IsRevoked));
            cmd.Parameters.Add(new NpgsqlParameter<DateTime>("created", wak.Created));
            cmd.Parameters.Add(new NpgsqlParameter<DateTime>("expiry", wak.Expiry));
        }

        void SetBinaryParameter(NpgsqlCommand cmd, string parameterName, object value)
        {
            if (value != null)
                using (var ms = new MemoryStream(1000))
                {
                    formatter.Serialize(ms, value);
                    cmd.Parameters.AddWithValue(parameterName, ms.GetBuffer());
                }
            else
                cmd.Parameters.AddWithValue(parameterName, DBNull.Value);

        }

        /// <summary>
        /// Where an object is stored in the DB - deserialize
        /// </summary>
        /// <typeparam name="T">The class type to deserialize</typeparam>
        /// <param name="dataReader"></param>
        /// <param name="ordinal"></param>
        /// <returns></returns>
        T DeserializeField<T>(DbDataReader dataReader, int ordinal) where T : class
        {
            if (dataReader.IsDBNull(ordinal)) return null;

            byte[] buffer = (byte[])dataReader.GetValue(ordinal);

            using (var ms = new MemoryStream(buffer))
            {
                return formatter.Deserialize(ms) as T;
            }
        }

        /// <summary>
        /// Returns a wait time slightly varied around the timeout to avoid everything ending at once
        /// </summary>
        /// <returns></returns>
        private int GetWaitTime() =>
            random.Next(
                ConnectionPoolWaitTimeInMilliseconds - WaitTimeVarianceInMilliseconds,
                ConnectionPoolWaitTimeInMilliseconds + WaitTimeVarianceInMilliseconds);

        protected virtual void Dispose(bool disposable)
        {
            if (conn == null) return;
            conn.Close();
            conn.Dispose();
            conn = null;
            connPool.Release();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
