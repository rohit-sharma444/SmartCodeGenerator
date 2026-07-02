using CodeGenerationEngine.Models;

namespace CodeGenerationEngine.Rules;

public static class DefaultTypeRules
{
    public static List<TypeInferenceRule> Create()
    {
        return
        [
            new() {
                TypeName = "bool",
                StartsWith = ["is", "has", "can", "should"]
            },

            new TypeInferenceRule
            {
                TypeName = "int",
                ExactMatches = ["id"],
                EndsWith = ["id"],
                Contains = ["qty", "quantity", "count"]
            },

            new TypeInferenceRule
            {
                TypeName = "decimal",
                Contains =
                [
                    "amount",
                    "price",
                    "cost",
                    "rate",
                    "tax",
                    "discount",
                    "total",
                    "balance",
                    "percentage",
                    "percent",
                    "fee",
                    "charge",
                    "commission",
                    "salary",
                    "wage",
                    "income",
                    "revenue",
                    "expense",
                    "profit",
                    "loss",
                    "margin",
                    "markup",
                    "interest",
                    "yield",
                    "return",
                    "value",
                    "score",
                    "rating",
                    "grade",
                    "level",
                    "rank",
                    "priority",
                    "weight",
                    "length",
                    "width",
                    "height",
                    "depth",
                    "size",
                    "volume",
                    "area",
                    "distance",
                    "speed",
                    "duration"
                ]
            },

            // FIX: Changed "DateTime" to "datetime" to match TypeValidator
            new TypeInferenceRule
            {
                TypeName = "datetime",
                Contains =
                [
                    "date",
                    "time",
                    "created",
                    "updated",
                    "modified",
                    "timestamp"
                ]
            },

            // FIX: Changed "Guid" to "guid" to match TypeValidator
            new TypeInferenceRule
            {
                TypeName = "guid",
                Contains = ["guid", "uuid", "reference", "id_ref"]
            }
        ];
    }
}
