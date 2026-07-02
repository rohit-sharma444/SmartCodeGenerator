using System.Data.SqlClient;
using System.Text;

namespace PROJECTBUILDER.DATABASE;
/// <summary>
/// Represents MSSQLData Access Layer.
/// </summary>

public class MSSQLDataAccessLayer : DataAccessLayer
{
    public MSSQLDataAccessLayer(SqlConnection connection) => dbConnection = connection;

    public override void Delete(Type? classType, List<object> data)
    {
        throw new NotImplementedException();
    }

    public override void Insert(Type? classType, List<object> data)
    {
        try
        {
            var dataToSave = BuildInsertSQL(classType, data);
            LogWriter.Write("Inserting data...");
            foreach (var d1 in dataToSave)
            {
                SqlCommand command = new SqlCommand(d1, dbConnection);
                command.ExecuteNonQuery();
            }
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex.Message.ToString());
        }
        finally
        {
            LogWriter.Write("Data inserted!");
        }
    }

    public override void Update(Type? classType, List<object> data)
    {
        throw new NotImplementedException();
    }


    /// <summary>
    /// This function is used to create a list of insert statement of sql.
    /// </summary>
    private List<string> BuildInsertSQL(Type? classType, List<object> data)
    {
        var result = new List<string>();
        try
        {
            LogWriter.Write("Preparing insert statement...");

            foreach (Object d1 in data)
            {
                StringBuilder sql = new StringBuilder("INSERT INTO " + classType.Name + " (");
                StringBuilder values = new StringBuilder("VALUES (");
                bool bFirst = true;

                for (var ctr = 0; ctr < d1.GetType().GetProperties().Count(); ctr++)
                {
                    if (bFirst)
                        bFirst = false;
                    else
                    {
                        sql.Append(", ");
                        values.Append(", ");
                    }

                    // Getting field name
                    sql.Append(d1.GetType().GetProperty(d1.GetType().GetProperties()[ctr].Name).Name);

                    if (d1.GetType().GetProperty(d1.GetType().GetProperties()[ctr].Name).GetValue(d1).GetType().Name == "String" ||
                        d1.GetType().GetProperty(d1.GetType().GetProperties()[ctr].Name).GetValue(d1).GetType().Name == "Smalldatetime")
                        values.Append("'");

                    //Getting value
                    values.Append(d1.GetType().GetProperty(d1.GetType().GetProperties()[ctr].Name).GetValue(d1));

                    if (d1.GetType().GetProperty(d1.GetType().GetProperties()[ctr].Name).GetValue(d1).GetType().Name == "String" ||
                        d1.GetType().GetProperty(d1.GetType().GetProperties()[ctr].Name).GetValue(d1).GetType().Name == "Smalldatetime")
                        values.Append("'");
                }
                sql.Append(") ");
                sql.Append(values.ToString());
                sql.Append(")");
                result.Add(sql.ToString());
            }
        }
        catch (Exception ex)
        {
            LogWriter.Write(ex.Message.ToString());
            return null;
        }
        finally
        {
            LogWriter.Write("Insert statment prepared to save!");
        }
        return result;
    }

}
