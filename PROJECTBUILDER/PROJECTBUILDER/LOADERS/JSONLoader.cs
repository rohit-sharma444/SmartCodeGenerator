using Newtonsoft.Json;
using PROJECTBUILDER.DTO;
using System.Collections;


namespace PROJECTBUILDER.LOADERS;
/// <summary>
/// Represents JSONLoader.
/// </summary>

public class JSONLoader : DataLoader
{
    public JSONLoader(string name) : base(name) { }

    /// <summary>
    /// Load meta data of a class (Fields and field types) from json
    /// </summary>
    /// <param name="fileName"></param>
    /// <returns></returns>
    public List<MetaDataDTO> MetaDataLoader(string fileName)
    {
        var metaDataList = new List<MetaDataDTO>();

        LogWriter.Write("Loading validation data.");
        using StreamReader reader = new(fileName);
        string json = reader.ReadToEnd();
        dynamic jsonObj = JsonConvert.DeserializeObject(json);
        try
        {
            for (int ctr = 0; ctr < jsonObj.metadata.metadata1.Count; ctr++)
            {
                var metaData = new MetaDataDTO();
                metaData.Field = jsonObj.metadata.metadata1[ctr].Field.ToString();

                var val = jsonObj.metadata.metadata1[ctr].FieldType.ToString();
                metaData.FieldType = GetDataType(val);

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
        using StreamReader reader = new(fileName);
        string json = reader.ReadToEnd();
        dynamic jsonObj = JsonConvert.DeserializeObject(json);
        try
        {
            for (int ctr = 0; ctr < jsonObj.metadata.metadata1.Count; ctr++)
            {
                var validator = new ValidatorDTO();
                validator.Field = jsonObj.metadata.metadata1[ctr].Field.ToString();

                validator.Rule = jsonObj.metadata.metadata1[ctr].Rule.ToString();

                validator.DataToValidate = jsonObj.metadata.metadata1[ctr].DataToValidate;

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
    /// Load data of a class from data.json based on class meta data.
    /// </summary>
    /// <param name="fileName"></param>
    /// <returns></returns>
    public (Type, List<object>) BusinessDataLoader(string fileName, List<MetaDataDTO> metaData)
    {
        try
        {
            var dynClass = new List<object>();
            Type? typeOfClass = typeof(string);

            using StreamReader reader = new(fileName);

            string jsonString = reader.ReadToEnd();

            dynamic jsonObject = JsonConvert.DeserializeObject(jsonString);

            foreach (var row in jsonObject.data.rows)
            {
                var classObject = CreateClassObject(metaData);
                Type TP = classObject.GetType();
                typeOfClass = TP;
                int ctr = 0;

                foreach (var column in row.columns)
                {
                    switch (TP.GetProperty(metaData[ctr].Field).PropertyType.Name)
                    {
                        case "Int64":
                            SetValue(TP.GetProperty(metaData[ctr].Field), classObject, column[metaData[ctr].Field].Value);
                            break;
                        case "Double":
                            SetValue(TP.GetProperty(metaData[ctr].Field), classObject, column[metaData[ctr].Field.ToUpper()].Value);
                            break;
                        case "String":
                            SetValue(TP.GetProperty(metaData[ctr].Field), classObject, column[metaData[ctr].Field.ToUpper()].Value);
                            break;
                        case "Float":
                            SetValue(TP.GetProperty(metaData[ctr].Field), classObject, column[float.Parse(metaData[ctr].Field)]);
                            break;
                        case "Boolean":
                            SetValue(TP.GetProperty(metaData[ctr].Field), classObject, column[bool.Parse(metaData[ctr].Field)]);
                            break;
                        case "Decimal":
                            SetValue(TP.GetProperty(metaData[ctr].Field), classObject, column[decimal.Parse(metaData[ctr].Field)]);
                            break;
                        case "DateTime":
                            SetValue(TP.GetProperty(metaData[ctr].Field), classObject, column[DateTime.Parse(metaData[ctr].Field)]);
                            break;
                    }
                    ctr++;
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
