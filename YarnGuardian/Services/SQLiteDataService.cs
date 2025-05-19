using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace YarnGuardian.Services
{
    /// <summary>
    /// SQLite 数据服务，负责side_value表的创建、插入和查询
    /// </summary>
    public class SQLiteDataService
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public SQLiteDataService(string dbFileName = "yarn_guardian.db")
        {
            _dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dbFileName);
            _connectionString = $"Data Source={_dbPath}";
            EnsureDatabaseAndTable();
        }

        /// <summary>
        /// 确保数据库和side_value表存在
        /// </summary>
        private void EnsureDatabaseAndTable()
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS side_value (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    side_number INTEGER NOT NULL,
    value INTEGER NOT NULL
);";
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 插入一条记录
        /// </summary>
        public async Task InsertSideValueAsync(int sideNumber, int value)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT INTO side_value (side_number, value) VALUES (@side_number, @value)";
                cmd.Parameters.AddWithValue("@side_number", sideNumber);
                cmd.Parameters.AddWithValue("@value", value);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// 批量替换插入（事务），先删除该边号的旧数据再插入新数据
        /// mysql 查询到的断头数据 缓存到sqlite
        /// </summary>
        public async Task ReplaceSideValuesAsync(int sideNumber, IEnumerable<int> values)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var transaction = connection.BeginTransaction())
                {
                    // 删除旧数据
                    var delCmd = connection.CreateCommand();
                    delCmd.CommandText = "DELETE FROM side_value WHERE side_number = @side_number";
                    delCmd.Parameters.AddWithValue("@side_number", sideNumber);
                    await delCmd.ExecuteNonQueryAsync();

                    // 插入新数据
                    foreach (var value in values)
                    {
                        var cmd = connection.CreateCommand();
                        cmd.CommandText = "INSERT INTO side_value (side_number, value) VALUES (@side_number, @value)";
                        cmd.Parameters.AddWithValue("@side_number", sideNumber);
                        cmd.Parameters.AddWithValue("@value", value);
                        await cmd.ExecuteNonQueryAsync();
                    }
                    transaction.Commit();
                }
            }
        }

        /// <summary>
        /// 查询某个边号的所有value
        /// </summary>
        public async Task<int[]> GetValuesBySideNumberAsync(int sideNumber)
        {
            var result = new List<int>();
            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT value FROM side_value WHERE side_number = @side_number";
                cmd.Parameters.AddWithValue("@side_number", sideNumber);
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        result.Add(reader.GetInt32(0));
                    }
                }
            }
            return result.ToArray();
        }

        /// <summary>
        /// 缓存 switch_point_id（先删后插）
        /// </summary>
        public async Task ReplaceSwitchPointIdBySideNumberAsync(string sideNumber, string switchPointId)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var transaction = connection.BeginTransaction())
                {
                    // 创建表（如不存在）
                    var createCmd = connection.CreateCommand();
                    createCmd.CommandText = @"CREATE TABLE IF NOT EXISTS switch_point_cache (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        side_number TEXT NOT NULL,
                        switch_point_id TEXT
                    );";
                    createCmd.ExecuteNonQuery();

                    // 删除旧数据
                    var delCmd = connection.CreateCommand();
                    delCmd.CommandText = "DELETE FROM switch_point_cache WHERE side_number = @side_number";
                    delCmd.Parameters.AddWithValue("@side_number", sideNumber);
                    await delCmd.ExecuteNonQueryAsync();

                    // 插入新数据
                    var insCmd = connection.CreateCommand();
                    insCmd.CommandText = "INSERT INTO switch_point_cache (side_number, switch_point_id) VALUES (@side_number, @switch_point_id)";
                    insCmd.Parameters.AddWithValue("@side_number", sideNumber);
                    insCmd.Parameters.AddWithValue("@switch_point_id", switchPointId ?? "");
                    await insCmd.ExecuteNonQueryAsync();

                    transaction.Commit();
                }
            }
        }

        /// <summary>
        /// 缓存 wait_point_id（先删后插）
        /// </summary>
        public async Task ReplaceWaitPointIdByMachineIdAsync(string machineId, string waitPointId)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var transaction = connection.BeginTransaction())
                {
                    // 创建表（如不存在）
                    var createCmd = connection.CreateCommand();
                    createCmd.CommandText = @"CREATE TABLE IF NOT EXISTS wait_point_cache (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        machine_id TEXT NOT NULL,
                        wait_point_id TEXT
                    );";
                    createCmd.ExecuteNonQuery();

                    // 删除旧数据
                    var delCmd = connection.CreateCommand();
                    delCmd.CommandText = "DELETE FROM wait_point_cache WHERE machine_id = @machine_id";
                    delCmd.Parameters.AddWithValue("@machine_id", machineId);
                    await delCmd.ExecuteNonQueryAsync();

                    // 插入新数据
                    var insCmd = connection.CreateCommand();
                    insCmd.CommandText = "INSERT INTO wait_point_cache (machine_id, wait_point_id) VALUES (@machine_id, @wait_point_id)";
                    insCmd.Parameters.AddWithValue("@machine_id", machineId);
                    insCmd.Parameters.AddWithValue("@wait_point_id", waitPointId ?? "");
                    await insCmd.ExecuteNonQueryAsync();

                    transaction.Commit();
                }
            }
        }

        /// <summary>
        /// 缓存某边号对应的所有distance_value到sqlite（先删后插）
        /// </summary>
        public async Task ReplaceSpindleDistanceValuesAsync(string sideNumber, float[] distanceValues)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var transaction = connection.BeginTransaction())
                {
                    // 创建表（如不存在）
                    var createCmd = connection.CreateCommand();
                    createCmd.CommandText = @"CREATE TABLE IF NOT EXISTS spindle_distance_cache (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        side_number TEXT NOT NULL,
                        distance_value REAL
                    );";
                    createCmd.ExecuteNonQuery();

                    // 删除旧数据
                    var delCmd = connection.CreateCommand();
                    delCmd.CommandText = "DELETE FROM spindle_distance_cache WHERE side_number = @side_number";
                    delCmd.Parameters.AddWithValue("@side_number", sideNumber);
                    await delCmd.ExecuteNonQueryAsync();

                    // 插入新数据
                    foreach (var value in distanceValues)
                    {
                        var insCmd = connection.CreateCommand();
                        insCmd.CommandText = "INSERT INTO spindle_distance_cache (side_number, distance_value) VALUES (@side_number, @distance_value)";
                        insCmd.Parameters.AddWithValue("@side_number", sideNumber);
                        insCmd.Parameters.AddWithValue("@distance_value", value);
                        await insCmd.ExecuteNonQueryAsync();
                    }
                    transaction.Commit();
                }
            }
        }
    }
}
