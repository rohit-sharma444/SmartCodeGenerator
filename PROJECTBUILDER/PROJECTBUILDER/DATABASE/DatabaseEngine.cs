using PROJECTBUILDER.DTO;
using PROJECTBUILDER.ENUM;
using System.Xml;

namespace PROJECTBUILDER.DATABASE;
/// <summary>
/// Represents Database Engine.
/// </summary>

public class DatabaseEngine
{
    public DatabaseEngine(DatabaseTypeEnum dbType)
    {
        switch (dbType)
        {
            case DatabaseTypeEnum.MSSQL:
                var dbConnector = new DatabaseConnector(GetDatabaseConnection());
                var mssql = new MSSQLDataAccessLayer(dbConnector.GetConnection());
                break;
            case DatabaseTypeEnum.ORACLE:
                break;
            case DatabaseTypeEnum.MYSQL:
                break;
            case DatabaseTypeEnum.MSACCESS:
                break;
        }
    }

    private DatabaseConnectionDTO GetDatabaseConnection()
    {
        var result = new DatabaseConnectionDTO();

        XmlDocument xdc = new XmlDocument();
        //xdc.Load(Directory.GetCurrentDirectory() + "\\DATABASE\\DatabaseConnection.xml");
        xdc.Load("C:\\PROJECTBUILDER\\PROJECTBUILDER\\DATABASE\\DatabaseConnection.xml");
        XmlNodeList xnlNodes = xdc.SelectNodes("DATABASE");
        try
        {
            foreach (XmlNode xnlNode in xnlNodes)
            {
                XmlElement element = (XmlElement)xnlNode;
                result.DatabaseType = Enum.Parse<DatabaseTypeEnum>(element.GetElementsByTagName("FIELDS")[0].ChildNodes[0].InnerText);

                result.Server = element.GetElementsByTagName("FIELDS")[0].ChildNodes[1].InnerText;

                result.DataBase = element.GetElementsByTagName("FIELDS")[0].ChildNodes[2].InnerText;

                result.User = element.GetElementsByTagName("FIELDS")[0].ChildNodes[3].InnerText;

                result.Password = element.GetElementsByTagName("FIELDS")[0].ChildNodes[4].InnerText;

                result.Owner = element.GetElementsByTagName("FIELDS")[0].ChildNodes[5].InnerText;
            }
        }
        catch (Exception ex)
        {
            LogWriter.Write("Failed GetDatabaseConnection " + ex.Message.ToString());
        }
        finally
        {
            LogWriter.Write("GetDatabaseConnection sucessfull. ");
        }
        return result;
    }
}
