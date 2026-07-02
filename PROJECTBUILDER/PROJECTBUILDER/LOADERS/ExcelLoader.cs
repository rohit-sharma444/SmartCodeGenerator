using Microsoft.Office.Interop.Excel;
using PROJECTBUILDER.DTO;
using System.Collections;
using Excel = Microsoft.Office.Interop.Excel;

namespace PROJECTBUILDER.LOADERS;
/// <summary>
/// Represents Excel Loader.
/// </summary>

public class ExcelLoader : DataLoader
{
    public ExcelLoader(string name) : base(name) { }

    /// <summary>
    /// Load meta data of a class (Fields and field types) from excel
    /// </summary>
    /// <param name="fileName"></param>
    /// <returns></returns>
    public List<MetaDataDTO> MetaDataLoader(string fileName)
    {
        var metaDataList = new List<MetaDataDTO>();

        Application excel = new Application();
        Workbook wb;
        Worksheet ws;
        wb = excel.Workbooks.Open(fileName);
        ws = (Worksheet)wb.Sheets["metadata"];

        int rowCount = ws.UsedRange.Rows.Count;

        try
        {
            for (int i = 1; i <= rowCount; i++)
            {
                var metaData = new MetaDataDTO();
                if (ws.Cells[i, 1] != null)
                {
                    Excel.Range cell = (Excel.Range)ws.Cells[i, 1];
                    metaData.Field = (string)cell.Value;
                }

                if (ws.Cells[i, 2] != null)
                {
                    Excel.Range cell = (Excel.Range)ws.Cells[i, 2];
                    metaData.FieldType = GetDataType((string)cell.Value);
                }

                //if (ws.Cells[i, 3] != null)
                //{
                //    metaData.Rules = ((Excel.Range)ws.Cells[i, 3]).Value?.ToString().Trim().Split(',');
                //}

                metaDataList.Add(metaData);
            }
        }
        catch (Exception ex)
        {
            LogWriter.Write("Failed loading meta data with error " + ex.Message.ToString());
            wb.Close(0);
            excel.Quit();
        }
        finally
        {
            LogWriter.Write("Sucessfully loaded meta data.");
            wb.Close(0);
            excel.Quit();
        }
        return metaDataList;
    }


    public List<ValidatorDTO> ValidatorLoader(string fileName)
    {
        var validators = new List<ValidatorDTO>();

        Application excel = new Application();
        Workbook wb;
        Worksheet ws;
        wb = excel.Workbooks.Open(fileName);
        ws = (Worksheet)wb.Sheets["validation"];

        int rowCount = ws.UsedRange.Rows.Count;

        try
        {
            for (int i = 1; i <= rowCount; i++)
            {
                var validator = new ValidatorDTO();
                if (ws.Cells[i, 1] != null)
                {
                    Excel.Range cell = (Excel.Range)ws.Cells[i, 1];
                    validator.Field = (string)cell.Value;
                }

                if (ws.Cells[i, 2] != null)
                {
                    Excel.Range cell = (Excel.Range)ws.Cells[i, 2];
                    validator.Rule = (string)cell.Value;
                }

                if (ws.Cells[i, 3] != null)
                {
                    Excel.Range cell = (Excel.Range)ws.Cells[i, 3];
                    validator.DataToValidate = (string)cell.Value;
                }

                validators.Add(validator);
            }
        }
        catch (Exception ex)
        {
            LogWriter.Write("Failed loading validators data with error " + ex.Message.ToString());
            wb.Close(0);
            excel.Quit();
        }
        finally
        {
            LogWriter.Write("Sucessfully loaded validators data.");
            //wb.Close(0);
            excel.Quit();
        }
        return validators;
    }



    /// <summary>
    /// Load data of a class from excel[sheet2] based on class meta data from [sheet1].
    /// </summary>
    /// <param name="fileName"></param>
    /// <returns></returns>
    public (Type, List<object>) BusinessDataLoader(string fileName, List<MetaDataDTO> metaData)
    {
        Application excel = new Application();
        Workbook wb;
        Worksheet ws;
        try
        {
            wb = excel.Workbooks.Open(fileName);
            ws = (Worksheet)wb.Sheets["data"];

            int rowCount = ws.UsedRange.Rows.Count;
            int colCount = ws.UsedRange.Columns.Count;

            var dynClass = new List<object>();
            Type? typeOfClass = typeof(string);

            for (int i = 1; i <= rowCount; i++)
            {
                var classObject = CreateClassObject(metaData);
                Type TP = classObject.GetType();
                typeOfClass = TP;

                for (int j = 1; j <= colCount; j++)
                {
                    Excel.Range cell = (Excel.Range)ws.Cells[i, j];
                    var valueToSet = cell.Value.ToString();

                    switch (TP.GetProperty(metaData[j - 1].Field).PropertyType.Name)
                    {
                        case "Int64":
                            SetValue(TP.GetProperty(metaData[j - 1].Field), classObject, int.Parse(valueToSet));
                            break;
                        case "Double":
                            SetValue(TP.GetProperty(metaData[j - 1].Field), classObject, double.Parse(valueToSet));
                            break;
                        case "String":
                            SetValue(TP.GetProperty(metaData[j - 1].Field), classObject, valueToSet);
                            break;
                        case "Float":
                            SetValue(TP.GetProperty(metaData[j - 1].Field), classObject, float.Parse(valueToSet));
                            break;
                        case "Boolean":
                            SetValue(TP.GetProperty(metaData[j - 1].Field), classObject, bool.Parse(valueToSet));
                            break;
                        case "Decimal":
                            SetValue(TP.GetProperty(metaData[j - 1].Field), classObject, decimal.Parse(valueToSet));
                            break;
                        case "DateTime":
                            SetValue(TP.GetProperty(metaData[j - 1].Field), classObject, DateTime.Parse(valueToSet));
                            break;
                    }
                }
                dynClass.Add(classObject);
            }
            wb.Close(0);
            return (typeOfClass, dynClass);
        }
        catch (Exception ex)
        {
            LogWriter.Write("Failed loading business data with error " + ex.Message.ToString());
            excel.Quit();
        }
        finally
        {
            LogWriter.Write("Sucessfully loaded business data.");
            excel.Quit();
        }
        return (null, null);
    }

}
