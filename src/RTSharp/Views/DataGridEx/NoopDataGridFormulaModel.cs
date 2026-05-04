using System;

using Avalonia.Controls;
using Avalonia.Controls.DataGridFormulas;

using ProDataGrid.FormulaEngine;

namespace RTSharp.Views.DataGridEx;

public sealed class NoopDataGridFormulaModel : IDataGridFormulaModel
{
    public event EventHandler<DataGridFormulaInvalidatedEventArgs>? Invalidated
    {
        add { }
        remove { }
    }

    public int FormulaVersion => 0;

    public FormulaCalculationMode CalculationMode { get; set; }

    public void Attach(DataGrid grid)
    {
    }

    public void Detach()
    {
    }

    public object? Evaluate(object item, DataGridFormulaColumnDefinition column) => null;

    public void Invalidate()
    {
    }

    public void Recalculate()
    {
    }

    public string? GetCellFormula(object item, DataGridFormulaColumnDefinition column) => null;

    public bool TrySetCellFormula(object item, DataGridFormulaColumnDefinition column, string? formulaText, out string? error)
    {
        error = null;
        return string.IsNullOrEmpty(formulaText);
    }
}
