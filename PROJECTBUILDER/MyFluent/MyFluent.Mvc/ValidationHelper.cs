using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Web.Mvc;
using System.Linq.Expressions;

using MyFluent;
using MyFluent.Framework;

namespace System.Web.Mvc.Html
{
    public static class MyFluentClientValidationHelper
    {
        public static MvcHtmlString MyFluentClientValidation<TValidator, TModel>(
                                                        this HtmlHelper<TModel> helper, 
                                                        TValidator validator, 
                                                        bool submitHandler = true)
            where TValidator : Validator<TModel>, new()
        {
            return new MvcHtmlString(validator.GetJQueryRules(submitHandler));
        }
    }
}
