using System.Collections;
using System.Xml;
using PROJECTBUILDER.DTO;

namespace PROJECTBUILDER.LOADERS;
/// <summary>
/// Represents XMLLoader.
/// </summary>

public class XMLLoader : DataLoader
{
    public XMLLoader(string name) : base(name)
    {
    }

    //<summary>
    //Load meta data of a class (Fields and field types) from xml
    //</summary>
    public List<MetaDataDTO> MetaDataLoader(string fileName)
    {
        var metaDataList = new List<MetaDataDTO>();
        XmlDocument xdc = new XmlDocument();
        xdc.Load(fileName);
        XmlNodeList xnlNodes = xdc.SelectNodes("METADATA");
        try
        {
            foreach (XmlNode xnlNode in xnlNodes)
            {
                XmlElement element = (XmlElement)xnlNode;
                for (var ctr = 0; ctr < element.GetElementsByTagName("FIELDS")[0].ChildNodes.Count; ctr++)
                {
                    var metaData = new MetaDataDTO();
                    metaData.Field = element.GetElementsByTagName("FIELDS")[0].ChildNodes[ctr].InnerText;
                    metaData.FieldType = GetDataType(element.GetElementsByTagName("FIELDTYPES")[0].ChildNodes[ctr].InnerText);
                    metaDataList.Add(metaData);
                }
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

        XmlDocument xdc = new XmlDocument();
        xdc.Load(fileName);
        XmlNodeList xnlNodes = xdc.SelectNodes("VALIDATORS");
        try
        {
            foreach (XmlNode xnlNode in xnlNodes)
            {
                XmlElement element = (XmlElement)xnlNode;
                for (var ctr = 0; ctr < element.GetElementsByTagName("FIELDS")[0].ChildNodes.Count; ctr++)
                {
                    var validator = new ValidatorDTO();
                    validator.Field = element.GetElementsByTagName("FIELDS")[0].ChildNodes[ctr].InnerText;
                    validator.Rule = element.GetElementsByTagName("RULES")[0].ChildNodes[ctr].InnerText;
                    validator.DataToValidate = element.GetElementsByTagName("DATAVALIDATION")[0].ChildNodes[ctr].InnerText;

                    validators.Add(validator);
                }
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
    /// Load data of a class from data.xml based on class meta data.
    /// </summary>
    /// <param name="fileName"></param>
    /// <returns></returns>
    public (Type, List<object>) BusinessDataLoader(string fileName, List<MetaDataDTO> metaData)
    {
        try
        {
            XmlDocument xdc = new XmlDocument();
            xdc.Load(fileName);
            XmlNodeList xnlNodes = xdc.SelectNodes("DATA");
            var dynClass = new List<object>();
            Type? typeOfClass = typeof(string);

            foreach (XmlNode xnlNode in xnlNodes)
            {
                XmlElement element = (XmlElement)xnlNode;
                var recordCount = element.GetElementsByTagName("RECORD").Count;

                for (var recordNumber = 1; recordNumber <= recordCount; recordNumber++)
                {
                    var classObject = CreateClassObject(metaData);
                    Type TP = classObject.GetType();
                    typeOfClass = TP;

                    var fieldCount = element.GetElementsByTagName("RECORD")[recordNumber - 1].ChildNodes.Count;

                    for (var fieldNumber = 1; fieldNumber <= fieldCount; fieldNumber++)
                    {
                        var valueToSet = element.GetElementsByTagName("RECORD")[recordNumber - 1].ChildNodes[fieldNumber - 1].InnerText;

                        switch (TP.GetProperty(metaData[fieldNumber - 1].Field).PropertyType.Name)
                        {
                            case "Int64":
                                SetValue(TP.GetProperty(metaData[fieldNumber - 1].Field), classObject, int.Parse(valueToSet));
                                break;
                            case "Double":
                                SetValue(TP.GetProperty(metaData[fieldNumber - 1].Field), classObject, double.Parse(valueToSet));
                                break;
                            case "String":
                                SetValue(TP.GetProperty(metaData[fieldNumber - 1].Field), classObject, valueToSet);
                                break;
                            case "Float":
                                SetValue(TP.GetProperty(metaData[fieldNumber - 1].Field), classObject, float.Parse(valueToSet));
                                break;
                            case "Boolean":
                                SetValue(TP.GetProperty(metaData[fieldNumber - 1].Field), classObject, bool.Parse(valueToSet));
                                break;
                            case "Decimal":
                                SetValue(TP.GetProperty(metaData[fieldNumber - 1].Field), classObject, decimal.Parse(valueToSet));
                                break;
                            case "DateTime":
                                SetValue(TP.GetProperty(metaData[fieldNumber - 1].Field), classObject, DateTime.Parse(valueToSet));
                                break;
                        }
                    }
                    dynClass.Add(classObject);
                }
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
