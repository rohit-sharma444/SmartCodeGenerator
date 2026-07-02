using System.Data.SqlClient;

namespace PROJECTBUILDER.DATABASE;
/// <summary>
/// Represents Data Access Layer.
/// </summary>

public abstract class DataAccessLayer
{
    public SqlConnection dbConnection;
    public abstract void Insert(Type? classType, List<object> data);
    public abstract void Update(Type? classType, List<object> data);
    public abstract void Delete(Type? classType, List<object> data);
}
