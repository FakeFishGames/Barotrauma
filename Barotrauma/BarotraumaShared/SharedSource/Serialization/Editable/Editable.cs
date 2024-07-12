using System;

namespace Barotrauma;

[AttributeUsage(AttributeTargets.Property)]
class Editable : Attribute
{
    public int MaxLength;
    public int DecimalCount = 1;

    public int MinValueInt = int.MinValue, MaxValueInt = int.MaxValue;
    public float MinValueFloat = float.MinValue, MaxValueFloat = float.MaxValue;
    public bool ForceShowPlusMinusButtons;
    public float ValueStep;
    
    /// <summary>
    /// Should the value customized in the editor be applied to the new item swapped in place of this item.
    /// Used e.g. for transferring the auto operate properties from one turret to another installed on place of it.
    /// </summary>
    public bool TransferToSwappedItem;

    /// <summary>
    /// Labels of the components of a vector property (defaults to x,y,z,w)
    /// </summary>
    public string[] VectorComponentLabels;

    /// <summary>
    /// If a translation can't be found for the property name, this tag is used instead
    /// </summary>
    public string FallBackTextTag;

    /// <summary>
    /// Currently implemented only for int and bool fields. TODO: implement the remaining types (SerializableEntityEditor)
    /// </summary>
    public bool ReadOnly;

    public Editable(int maxLength = 20)
    {
        MaxLength = maxLength;
    }

    public Editable(int minValue, int maxValue)
    {
        MinValueInt = minValue;
        MaxValueInt = maxValue;
    }

    public Editable(float minValue, float maxValue, int decimals = 1)
    {
        MinValueFloat = minValue;
        MaxValueFloat = maxValue;
        DecimalCount = decimals;
    }
}

[AttributeUsage(AttributeTargets.Property)]
sealed class InGameEditable : Editable
{
}

