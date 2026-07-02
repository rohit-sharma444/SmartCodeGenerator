
using PROJECTBUILDER.ENUM;

namespace PROJECTBUILDER.DTO
{
/// <summary>
/// Represents Database Connection DTO.
/// </summary>
    public class DatabaseConnectionDTO
    {
        public DatabaseTypeEnum DatabaseType { get; set; }
        public string Server { get; set; }
        public string DataBase { get; set; }
        public string User { get; set; }
        public string Password { get; set; }
        public string Owner { get; set; }
        public bool UseTrustedConnection { get; set; }
    }
}
