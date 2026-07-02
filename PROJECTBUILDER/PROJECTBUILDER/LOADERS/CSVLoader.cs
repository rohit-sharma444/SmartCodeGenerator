using Csv;
using PROJECTBUILDER.DTO;
using System.Collections;


namespace PROJECTBUILDER.LOADERS;
/// <summary>
/// Represents CSVLoader.
/// </summary>

public class CSVLoader : DataLoader
{
    public CSVLoader(string name) : base(name)
    {
    }

    // <summary>
    // Load meta data of a class (Fields and field types) from CSV
    // </summary>
    // <param name = "fileName" ></ param >
    // < returns ></ returns >
    public List<MetaDataDTO> MetaDataLoader(string fileName)
    {
        var metaDataList = new List<MetaDataDTO>();
        var csvFile = File.ReadAllText(fileName);
        var options = new CsvOptions() { HeaderMode = HeaderMode.HeaderAbsent };
        int row = 0;

        try
        {
            foreach (var line in CsvReader.ReadFromText(csvFile, options))
            {
                var metaData = new MetaDataDTO();
                metaData.Field = line.Values[0];
                metaData.FieldType = GetDataType(line.Values[1]);
                metaDataList.Add(metaData);
            }
        }
        catch (Exception ex)
        {
            LogWriter.Write("Failed loading meta data with error " + ex.Message.ToString());
        }
        finally
        {
            LogWriter.Write("Sucessfully loaded meta data.");
        }
        return metaDataList;
    }


    public List<ValidatorDTO> ValidatorLoader(string fileName)
    {
        var validators = new List<ValidatorDTO>();

        LogWriter.Write("Loading validation data.");

        var csvFile = File.ReadAllText(fileName);
        var options = new CsvOptions() { HeaderMode = HeaderMode.HeaderAbsent };
        int row = 0;
        try
        {
            foreach (var line in CsvReader.ReadFromText(csvFile, options))
            {
                var validator = new ValidatorDTO();
                validator.Field = line.Values[0];
                validator.Rule = line.Values[1];
                validator.DataToValidate = line.Values[2];
                validators.Add(validator);
            }
        }
        catch (Exception ex)
        {
            LogWriter.Write("Failed loading validation data with error " + ex.Message.ToString());
        }
        finally
        {
            LogWriter.Write("Sucessfully loaded validation data.");
        }
        return validators;
    }


    /// <summary>
    /// Load data of a class from data.csv based on class meta data.
    /// </summary>
    /// <param name="fileName"></param>
    /// <returns></returns>
    public (Type, List<object>) BusinessDataLoader(string fileName, List<MetaDataDTO> metaData)
    {
        var dynClass = new List<object>();
        Type? typeOfClass = typeof(string);

        var csvFile = File.ReadAllText(fileName);
        var options = new CsvOptions() { HeaderMode = HeaderMode.HeaderAbsent };

        try
        {
            foreach (var line in CsvReader.ReadFromText(csvFile, options))
            {
                var classObject = CreateClassObject(metaData);
                Type TP = classObject.GetType();
                typeOfClass = TP;

                for (int colidx = 0; colidx < line.ColumnCount; colidx++)
                {
                    var valueToSet = line.Values[colidx];

                    switch (TP.GetProperty(metaData[colidx].Field).PropertyType.Name)
                    {
                        case "Int64":
                            SetValue(TP.GetProperty(metaData[colidx].Field), classObject, int.Parse(valueToSet));
                            break;
                        case "Double":
                            SetValue(TP.GetProperty(metaData[colidx].Field), classObject, double.Parse(valueToSet));
                            break;
                        case "String":
                            SetValue(TP.GetProperty(metaData[colidx].Field), classObject, valueToSet);
                            break;
                        case "Float":
                            SetValue(TP.GetProperty(metaData[colidx].Field), classObject, float.Parse(valueToSet));
                            break;
                        case "Boolean":
                            SetValue(TP.GetProperty(metaData[colidx].Field), classObject, bool.Parse(valueToSet));
                            break;
                        case "Decimal":
                            SetValue(TP.GetProperty(metaData[colidx].Field), classObject, decimal.Parse(valueToSet));
                            break;
                        case "DateTime":
                            SetValue(TP.GetProperty(metaData[colidx].Field), classObject, DateTime.Parse(valueToSet));
                            break;
                    }
                }
                dynClass.Add(classObject);
            }
            return (typeOfClass, dynClass);
        }
        catch (Exception ex)
        {
            LogWriter.Write("Failed loading business data with error " + ex.Message.ToString());
        }
        finally
        {
            LogWriter.Write("Sucessfully loaded business data.");
        }
        return (null, null);
    }
}
