// Decompiled with JetBrains decompiler
// Type: Sage.Common.Rules.DuplicateStringNotInCollectionRule
// Assembly: Sage.Common, Version=19.0.0.0, Culture=neutral, PublicKeyToken=b2daa66d74953d11
// MVID: 8E19D169-A760-428C-9A18-16E1BB4A7EE8
// Assembly location: C:\Sage2002023R2Assemblies\Sage.Common.dll

using Sage.Common.ProgrammingByContract;
using System.Collections;
using System.Reflection;
using System.Text;

#nullable disable
namespace Sage.Common.Rules
{
/// <summary>
/// Represents Duplicate String Not In Collection Rule.
/// </summary>
  public class DuplicateStringNotInCollectionRule : Sage.Common.Rules.Rule
  {
    private const string ITEM_EXISTS = "The string already exists in the collection.";
    private const string ITEM_DOESNOT_EXIST = "The string does not exist in the collection.";
    private object _oExcludeObject;
    private PropertyInfo[] _oPropertyInfos;
    private string[] _oPropertyValues;
    private IList _oObjects;
    private bool _bIsCaseSensitive;

    public static DuplicateStringNotInCollectionRule Rule(
      object excludeObject,
      PropertyInfo propertyInfo,
      string propertyValue,
      IList objects,
      bool isCaseSensitive)
    {
      Contract.PreCond(propertyInfo != (PropertyInfo) null, "The propertyInfo parameter must not be null");
      Contract.PreCond(propertyValue != null, "The propertyValue parameter must not be null");
      Contract.PreCond(objects != null, "The objects parameter must not be null");
      PropertyInfo[] propertyInfos = new PropertyInfo[1]
      {
        propertyInfo
      };
      string[] propertyValues = new string[1]
      {
        propertyValue
      };
      return new DuplicateStringNotInCollectionRule(excludeObject, propertyInfos, propertyValues, objects, isCaseSensitive);
    }

    protected DuplicateStringNotInCollectionRule(
      object excludeObject,
      PropertyInfo[] propertyInfos,
      string[] propertyValues,
      IList objects,
      bool isCaseSensitive)
    {
      this._oExcludeObject = excludeObject;
      this._oPropertyInfos = propertyInfos;
      this._oPropertyValues = propertyValues;
      this._oObjects = objects;
      this._bIsCaseSensitive = isCaseSensitive;
    }

    private bool IsInCollection()
    {
      bool flag = false;
      int length = this._oPropertyInfos.GetLength(0);
      foreach (object oObject in (IEnumerable) this._oObjects)
      {
        if (oObject != this._oExcludeObject)
        {
          int num = 0;
          for (int index = 0; index < length && string.Compare((string) this._oPropertyInfos[index].GetValue(oObject, (object[]) null), this._oPropertyValues[index], !this._bIsCaseSensitive) == 0; ++index)
            ++num;
          if (num == length)
          {
            flag = true;
            break;
          }
        }
      }
      return flag;
    }

    protected override bool InternalIsTrue()
    {
      this.Reset();
      int num = !this.IsInCollection() ? 1 : 0;
      if (num != 0)
        return num != 0;
      this.SetFailureIndicator("The string already exists in the collection.");
      return num != 0;
    }

    protected override bool InternalIsFalse()
    {
      this.Reset();
      int num = this.IsInCollection() ? 1 : 0;
      if (num != 0)
        return num != 0;
      this.SetFailureIndicator("The string does not exist in the collection.");
      return num != 0;
    }

    public override string ToString()
    {
      StringBuilder stringBuilder = new StringBuilder();
      stringBuilder.Append("DUPLICATE SHOULD NOT EXIST IN COLLECTION RULE:\n");
      stringBuilder.AppendFormat("\t{0}\n", (object) this._oExcludeObject.ToString());
      stringBuilder.AppendFormat("\t{0}\n", (object) this._oPropertyInfos.ToString());
      stringBuilder.AppendFormat("\t{0}\n", (object) this._oPropertyValues.ToString());
      foreach (object oObject in (IEnumerable) this._oObjects)
        stringBuilder.AppendFormat("\t{0}\n", (object) oObject.ToString());
      return stringBuilder.ToString() + base.ToString();
    }
  }
}
