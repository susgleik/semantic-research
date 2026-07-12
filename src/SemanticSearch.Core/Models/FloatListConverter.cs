using System.Globalization;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;

namespace SemanticSearch.Core.Models;

/// <summary>
/// Fuerza a que List&lt;float&gt; se guarde como DynamoDB List (L) de números.
/// El converter por defecto del SDK mapea List&lt;primitivo&gt; a Number Set (NS),
/// que no preserva orden y descarta valores duplicados — inaceptable para un
/// vector de embedding, donde el orden importa y los duplicados son comunes.
/// </summary>
public class FloatListConverter : IPropertyConverter
{
    public DynamoDBEntry ToEntry(object value)
    {
        var floats = (List<float>)value;
        var list = new DynamoDBList();
        foreach (var f in floats)
            list.Add(new Primitive(f.ToString(CultureInfo.InvariantCulture), saveAsNumeric: true));
        return list;
    }

    public object FromEntry(DynamoDBEntry entry)
    {
        var list = entry.AsDynamoDBList();
        return list.Entries.Select(e => (float)e.AsDouble()).ToList();
    }
}
