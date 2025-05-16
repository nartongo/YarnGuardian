using System;
using System.Data;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
using YarnGuardian.Common;

namespace YarnGuardian.Services
{
    /// <summary>
    /// MySQL数据库服务，处理数据库连接和操作
    /// </summary>
    public class MySqlDataService
    {
        private readonly ConfigService _configService;
        private MySqlConnection _connection;
        private bool _isConnected;
        
        /// <summary>
        /// 构造函数，注入ConfigService
        /// </summary>
        public MySqlDataService(ConfigService configService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        }
        
        /// <summary>
        /// 获取连接字符串
        /// </summary>
        private string GetConnectionString()
        {
            string server = _configService.GetMySqlServer();
            int port = _configService.GetMySqlPort();
            string database = _configService.GetMySqlDatabase();
            string username = _configService.GetMySqlUsername();
            string password = _configService.GetMySqlPassword();
            
            return $"Server={server};Port={port};Database={database};User ID={username};Password={password};CharSet=utf8;";
        }
        
        /// <summary>
        /// 异步连接到数据库
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            try
            {
                if (_isConnected && _connection != null && _connection.State == ConnectionState.Open)
                {
                    Console.WriteLine("[MySqlDataService] 已经连接到数据库");
                    return true;
                }
                
                string connectionString = GetConnectionString();
                _connection = new MySqlConnection(connectionString);
                
                Console.WriteLine("[MySqlDataService] 正在连接到数据库...");
                await _connection.OpenAsync();
                
                _isConnected = true;
                Console.WriteLine("[MySqlDataService] 数据库连接成功");
                
                // 可以发布连接成功的事件
                EventHub.Instance.Publish("DB_CONNECTED", DateTime.Now);
                
                return true;
            }
            catch (Exception ex)
            {
                _isConnected = false;
                Console.WriteLine($"[MySqlDataService] 数据库连接失败: {ex.Message}");
                
                // 可以发布连接失败的事件
                EventHub.Instance.Publish("DB_CONNECTION_FAILED", ex.Message);
                
                return false;
            }
        }
        
        /// <summary>
        /// 断开数据库连接
        /// </summary>
        public void Disconnect()
        {
            try
            {
                if (_connection != null && _connection.State == ConnectionState.Open)
                {
                    _connection.Close();
                    _connection.Dispose();
                    _isConnected = false;
                    Console.WriteLine("[MySqlDataService] 数据库连接已关闭");
                    
                    // 可以发布断开连接的事件
                    EventHub.Instance.Publish("DB_DISCONNECTED", DateTime.Now);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MySqlDataService] 关闭数据库连接时发生错误: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 执行非查询SQL语句（如INSERT、UPDATE、DELETE）
        /// </summary>
        /// <param name="sql">SQL语句</param>
        /// <param name="parameters">参数数组</param>
        /// <returns>受影响的行数</returns>
        public async Task<int> ExecuteNonQueryAsync(string sql, params MySqlParameter[] parameters)
        {
            if (!_isConnected)
            {
                await ConnectAsync();
            }
            
            try
            {
                using (MySqlCommand cmd = new MySqlCommand(sql, _connection))
                {
                    if (parameters != null && parameters.Length > 0)
                    {
                        cmd.Parameters.AddRange(parameters);
                    }
                    
                    return await cmd.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MySqlDataService] 执行SQL语句失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 执行查询并返回DataTable
        /// </summary>
        /// <param name="sql">SQL查询语句</param>
        /// <param name="parameters">参数数组</param>
        /// <returns>查询结果DataTable</returns>
        public async Task<DataTable> ExecuteQueryAsync(string sql, params MySqlParameter[] parameters)
        {
            if (!_isConnected)
            {
                await ConnectAsync();
            }
            
            try
            {
                using (MySqlCommand cmd = new MySqlCommand(sql, _connection))
                {
                    if (parameters != null && parameters.Length > 0)
                    {
                        cmd.Parameters.AddRange(parameters);
                    }
                    
                    using (MySqlDataAdapter adapter = new MySqlDataAdapter(cmd))
                    {
                        DataTable dataTable = new DataTable();
                        await Task.Run(() => adapter.Fill(dataTable));
                        return dataTable;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MySqlDataService] 执行查询失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 执行查询并返回第一行第一列的值
        /// </summary>
        /// <typeparam name="T">返回值类型</typeparam>
        /// <param name="sql">SQL查询语句</param>
        /// <param name="parameters">参数数组</param>
        /// <returns>查询结果值</returns>
        public async Task<T> ExecuteScalarAsync<T>(string sql, params MySqlParameter[] parameters)
        {
            if (!_isConnected)
            {
                await ConnectAsync();
            }
            
            try
            {
                using (MySqlCommand cmd = new MySqlCommand(sql, _connection))
                {
                    if (parameters != null && parameters.Length > 0)
                    {
                        cmd.Parameters.AddRange(parameters);
                    }
                    
                    object result = await cmd.ExecuteScalarAsync();
                    
                    if (result == null || result == DBNull.Value)
                    {
                        return default(T);
                    }
                    
                    return (T)Convert.ChangeType(result, typeof(T));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MySqlDataService] 执行查询失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 在事务中执行多个SQL操作
        /// </summary>
        /// <param name="actions">要在事务中执行的操作</param>
        /// <returns>是否成功</returns>
        public async Task<bool> ExecuteInTransactionAsync(Func<MySqlConnection, MySqlTransaction, Task> actions)
        {
            if (!_isConnected)
            {
                await ConnectAsync();
            }
            
            MySqlTransaction transaction = null;
            
            try
            {
                transaction = await _connection.BeginTransactionAsync();
                
                await actions(_connection, transaction);
                
                await transaction.CommitAsync();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MySqlDataService] 事务执行失败: {ex.Message}");
                
                if (transaction != null)
                {
                    await transaction.RollbackAsync();
                }
                
                return false;
            }
        }
    }
}