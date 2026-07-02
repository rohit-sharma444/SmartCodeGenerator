using PROJECTBUILDER.DTO;
using PROJECTBUILDER.ENUM;

namespace PROJECTBUILDER.LOADERS;
/// <summary>
/// Represents Data Loader.
/// </summary>

public class DataLoader : ClassBuilder
{
    private MetaDataDTO metaData;
    private string className;

    public DataLoader(string name) : base(name)
    {
        metaData = new MetaDataDTO();
        className = name;
        LogWriter.LogEnabled = true;
    }

    public DataHolderDTO LoadData(DataSourceEnum fileType, string metaDatafileName, string businessDataFileName)
    {
        var dataHolder = new DataHolderDTO();

        switch (fileType)
        {
            case DataSourceEnum.Excel:
                var excel = new ExcelLoader(className);
                LogWriter.Write("Loading meta data...");
                dataHolder.Metadata = excel.MetaDataLoader(metaDatafileName);
                LogWriter.Write("Loading business data...");
                dataHolder.BusinessData = excel.BusinessDataLoader(metaDatafileName, dataHolder.Metadata);
                dataHolder.Validators = excel.ValidatorLoader(metaDatafileName);
                return dataHolder;

            case DataSourceEnum.Json:
                var json = new JSONLoader(className);
                LogWriter.Write("Loading meta data...");
                dataHolder.Metadata = json.MetaDataLoader(metaDatafileName);
                LogWriter.Write("Loading business data...");
                dataHolder.BusinessData = json.BusinessDataLoader(businessDataFileName, dataHolder.Metadata);
                dataHolder.Validators = json.ValidatorLoader(metaDatafileName);
                return dataHolder;

            case DataSourceEnum.XML:
                var xml = new XMLLoader(className);
                LogWriter.Write("Loading meta data...");
                dataHolder.Metadata = xml.MetaDataLoader(metaDatafileName);
                LogWriter.Write("Loading business data...");
                dataHolder.BusinessData = xml.BusinessDataLoader(businessDataFileName, dataHolder.Metadata);
                dataHolder.Validators = xml.ValidatorLoader(metaDatafileName);
                return dataHolder;

            case DataSourceEnum.CSV:
                var csv = new CSVLoader(className);
                LogWriter.Write("Loading meta data...");
                dataHolder.Metadata = csv.MetaDataLoader(metaDatafileName);
                LogWriter.Write("Loading business data...");
                dataHolder.BusinessData = csv.BusinessDataLoader(businessDataFileName, dataHolder.Metadata);
                dataHolder.Validators = csv.ValidatorLoader(metaDatafileName);
                return dataHolder;
        }
        return null;
    }
}
