using Microsoft.Office.Interop.Excel;
using System;
using System.Collections;
namespace PROJECTBUILDER.RULES;
/// <summary>
/// Represents Member Rule.
/// </summary>

public class MemberRule : Rule
{
    private const string IS_MEMBER = "in list";
    private const string IS_NOT_MEMBER = "not in list";
    private IList _oList;
    private object _oValue;

    public static MemberRule Rule(object value, IList list) => new MemberRule(value, list);

    public static MemberRule Rule(object value, IList list, object ruleIdentifier)
    {
        MemberRule memberRule = new MemberRule(value, list);
        memberRule.Identifier = ruleIdentifier;
        return memberRule;
    }

    protected MemberRule(object value, IList list)
    {
        this._oValue = value;
        this._oList = list;
    }

    public override bool IsTrue()
    {
        bool flag = true;
        if (!this._oList.Contains(this._oValue))
        {
            if (this.FailureIndicator != null)
                this.SetFailureIndicator("not in list");
            flag = false;
        }
        return flag;
    }

    public override bool IsFalse()
    {
        bool flag = true;
        if (this._oList.Contains(this._oValue))
        {
            if (this.FailureIndicator != null)
                this.SetFailureIndicator("in list");
            flag = false;
        }
        return flag;
    }

    public string Name => "MemberRule";
}
