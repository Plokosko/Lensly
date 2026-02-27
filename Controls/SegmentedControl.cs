using System;
using System.Collections;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Lensly.Controls;

public class SegmentedControl : TemplatedControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsProperty =
        AvaloniaProperty.Register<SegmentedControl, IEnumerable?>(nameof(Items));

    public static readonly StyledProperty<object?> SelectedItemProperty =
        AvaloniaProperty.Register<SegmentedControl, object?>(nameof(SelectedItem), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<SegmentedControl, int>(nameof(SelectedIndex), -1, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<IBrush?> SelectionBrushProperty =
        AvaloniaProperty.Register<SegmentedControl, IBrush?>(nameof(SelectionBrush), new SolidColorBrush(Color.Parse("#007AFF")));

    public static readonly new StyledProperty<CornerRadius> CornerRadiusProperty =
        AvaloniaProperty.Register<SegmentedControl, CornerRadius>(nameof(CornerRadius), new CornerRadius(8));

    public IEnumerable? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public IBrush? SelectionBrush
    {
        get => GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    public new CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

    private ItemsControl? _itemsControl;
    private Border? _selectionIndicator;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _itemsControl = e.NameScope.Find<ItemsControl>("PART_ItemsControl");
        _selectionIndicator = e.NameScope.Find<Border>("PART_SelectionIndicator");
        
        if (_itemsControl != null)
        {
            _itemsControl.PointerPressed += OnItemPointerPressed;
            LayoutUpdated += (s, ev) => UpdateSelectionIndicator();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SelectedItemProperty)
        {
            UpdateSelectedIndex();
            UpdateSelectionIndicator();
            OnSelectionChanged();
        }
        else if (change.Property == SelectedIndexProperty)
        {
            UpdateSelectedItem();
            UpdateSelectionIndicator();
            OnSelectionChanged();
        }
        else if (change.Property == ItemsProperty)
        {
            if (change.OldValue is INotifyCollectionChanged oldCollection)
                oldCollection.CollectionChanged -= OnItemsCollectionChanged;
            if (change.NewValue is INotifyCollectionChanged newCollection)
                newCollection.CollectionChanged += OnItemsCollectionChanged;
            
            UpdateSelectedIndex();
            UpdateSelectionIndicator();
        }
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateSelectionIndicator();
    }

    private void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual visual)
        {
            // Find the item container
            var container = visual.FindAncestorOfType<ContentPresenter>(true);
            if (container == null)
            {
                // Fallback to DataContext check if visual is an element inside template
                if (visual.DataContext != null && _itemsControl != null)
                {
                    container = _itemsControl.ContainerFromItem(visual.DataContext) as ContentPresenter;
                }
            }

            if (container != null && _itemsControl != null)
            {
                var index = _itemsControl.IndexFromContainer(container);
                if (index >= 0)
                {
                    SelectedIndex = index;
                    e.Handled = true;
                }
            }
        }
    }

    private void UpdateSelectedIndex()
    {
        if (Items == null || SelectedItem == null) return;
        
        var index = 0;
        foreach (var item in Items)
        {
            if (Equals(item, SelectedItem))
            {
                SelectedIndex = index;
                return;
            }
            index++;
        }
        SelectedIndex = -1;
    }

    private void UpdateSelectedItem()
    {
        if (Items == null || SelectedIndex < 0)
        {
            SelectedItem = null;
            return;
        }

        var index = 0;
        foreach (var item in Items)
        {
            if (index == SelectedIndex)
            {
                SelectedItem = item;
                return;
            }
            index++;
        }
    }

    private void UpdateSelectionIndicator()
    {
        if (_itemsControl == null || _selectionIndicator == null) return;
        if (SelectedIndex < 0) 
        {
            _selectionIndicator.IsVisible = false;
            return;
        }

        var container = _itemsControl.ContainerFromIndex(SelectedIndex);
        if (container is Control control)
        {
            var newWidth = control.Bounds.Width;
            var newLeft = control.Bounds.Left;

            if (newWidth > 0 && (Math.Abs(_selectionIndicator.Width - newWidth) > 0.1 || Math.Abs(_selectionIndicator.Margin.Left - newLeft) > 0.1))
            {
                _selectionIndicator.Width = newWidth;
                _selectionIndicator.Margin = new Thickness(newLeft, 0, 0, 0);
                _selectionIndicator.IsVisible = true;
            }
        }
    }

    private void OnSelectionChanged()
    {
        SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(SelectedIndex));
    }
}

public class SelectionChangedEventArgs : EventArgs
{
    public int SelectedIndex { get; }

    public SelectionChangedEventArgs(int selectedIndex)
    {
        SelectedIndex = selectedIndex;
    }
}
