using PROJECTBUILDER.DTO;
using PROJECTBUILDER.ENUM;
using System;
using System.Data.SqlClient;
using System.Text;


namespace PROJECTBUILDER.DATABASE;

/// <summary>
/// Class provides database connection
/// </summary>
public class DatabaseConnector
{
    private StringBuilder _connectionString = new StringBuilder();
    private SqlConnection _con;

    public DatabaseConnector(DatabaseConnectionDTO dbConnection)
    {
        //_connectionString.Append(dbType);
        _connectionString.Append("Server=" + dbConnection.Server + ";");
        _connectionString.Append("database=" + dbConnection.DataBase + ";");
        _connectionString.Append("user=" + dbConnection.User + ";");
        _connectionString.Append("password=" + dbConnection.Password + ";");
        //_connectionString.Append(owner);
        //_connectionString.Append(trustedConn);
    }

    public SqlConnection GetConnection()
    {
        LogWriter.Write("Opening database...");
        try
        {
            _con = new SqlConnection(_connectionString.ToString());
            _con.Open();
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex.Message.ToString());
        }
        finally
        {
            LogWriter.Write("Sucessfully open database connection!");
        }
        return _con;
    }

    public void Close()
    {
        LogWriter.Write("Close database connection!");
        _con.Close();
    }
}
