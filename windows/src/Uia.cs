// Cliport v2.0 - UI Automation for the hand-drawn lists, so Narrator and other UI Automation screen readers read the rows.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Provider;
using System.Windows.Forms;

namespace Cliport
{
    /// <summary>The list itself: a UI Automation "List" whose children are the rows.</summary>
    [ComVisible(true)]
    sealed class UiaList : IRawElementProviderFragmentRoot, ISelectionProvider
    {
        readonly CardList o;
        public UiaList(CardList owner) { o = owner; }
        public UiaRow Row(int i) { return i >= 0 && i < o.Items.Count ? new UiaRow(o, this, i) : null; }

        public ProviderOptions ProviderOptions { get { return ProviderOptions.ServerSideProvider | ProviderOptions.UseComThreading; } }
        public IRawElementProviderSimple HostRawElementProvider { get { return AutomationInteropProvider.HostProviderFromHandle(o.Handle); } }
        public object GetPatternProvider(int patternId) { return patternId == SelectionPatternIdentifiers.Pattern.Id ? this : null; }
        public object GetPropertyValue(int id)
        {
            if (id == AutomationElementIdentifiers.ControlTypeProperty.Id) return ControlType.List.Id;
            if (id == AutomationElementIdentifiers.NameProperty.Id) return o.AccessibleName ?? L.T("רשימה", "List");
            if (id == AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id) return true;
            if (id == AutomationElementIdentifiers.IsControlElementProperty.Id || id == AutomationElementIdentifiers.IsContentElementProperty.Id) return true;
            return null;
        }
        public IRawElementProviderFragment Navigate(NavigateDirection d)
        {
            if (d == NavigateDirection.FirstChild) return Row(0);
            if (d == NavigateDirection.LastChild) return Row(o.Items.Count - 1);
            return null;
        }
        public int[] GetRuntimeId() { return null; }
        public System.Windows.Rect BoundingRectangle { get { return UiaRow.ToRect(o.RectangleToScreen(o.ClientRectangle)); } }
        public IRawElementProviderSimple[] GetEmbeddedFragmentRoots() { return null; }
        public void SetFocus() { o.Focus(); }
        public IRawElementProviderFragmentRoot FragmentRoot { get { return this; } }
        public IRawElementProviderFragment ElementProviderFromPoint(double x, double y)
        {
            int i = o.IndexFromPoint(o.PointToClient(new Point((int)x, (int)y)));
            return i >= 0 && i < o.Items.Count ? (IRawElementProviderFragment)Row(i) : this;
        }
        public IRawElementProviderFragment GetFocus() { return Row(o.SelectedIndex); }
        public IRawElementProviderSimple[] GetSelection() { var r = Row(o.SelectedIndex); return r == null ? new IRawElementProviderSimple[0] : new IRawElementProviderSimple[] { r }; }
        public bool CanSelectMultiple { get { return false; } }
        public bool IsSelectionRequired { get { return false; } }
    }

    /// <summary>One row: its name is the row's text, with the second line and the badge appended.</summary>
    [ComVisible(true)]
    sealed class UiaRow : IRawElementProviderFragment, ISelectionItemProvider
    {
        readonly CardList o; readonly UiaList root; readonly int i;
        public UiaRow(CardList owner, UiaList list, int index) { o = owner; root = list; i = index; }
        Card C { get { return i < o.Items.Count ? o.Items[i] as Card : null; } }
        public static System.Windows.Rect ToRect(Rectangle r) { return new System.Windows.Rect(r.X, r.Y, r.Width, r.Height); }

        public ProviderOptions ProviderOptions { get { return ProviderOptions.ServerSideProvider | ProviderOptions.UseComThreading; } }
        public IRawElementProviderSimple HostRawElementProvider { get { return null; } }
        public object GetPatternProvider(int patternId) { return patternId == SelectionItemPatternIdentifiers.Pattern.Id ? this : null; }
        public object GetPropertyValue(int id)
        {
            var c = C; if (c == null) return null;
            if (id == AutomationElementIdentifiers.ControlTypeProperty.Id) return ControlType.ListItem.Id;
            if (id == AutomationElementIdentifiers.NameProperty.Id) return c.Title;
            if (id == AutomationElementIdentifiers.HelpTextProperty.Id) return (c.Sub + (c.Badge != "" ? " " + c.Badge : "")).Trim();
            if (id == AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id) return true;
            if (id == AutomationElementIdentifiers.HasKeyboardFocusProperty.Id) return o.Focused && o.SelectedIndex == i;
            if (id == AutomationElementIdentifiers.IsEnabledProperty.Id) return !c.Hint;
            if (id == AutomationElementIdentifiers.IsControlElementProperty.Id || id == AutomationElementIdentifiers.IsContentElementProperty.Id) return true;
            return null;
        }
        public IRawElementProviderFragment Navigate(NavigateDirection d)
        {
            if (d == NavigateDirection.Parent) return root;
            if (d == NavigateDirection.NextSibling) return root.Row(i + 1);
            if (d == NavigateDirection.PreviousSibling) return root.Row(i - 1);
            return null;
        }
        public int[] GetRuntimeId() { return new[] { AutomationInteropProvider.AppendRuntimeId, i }; }
        public System.Windows.Rect BoundingRectangle { get { return i < o.Items.Count ? ToRect(o.RectangleToScreen(o.GetItemRectangle(i))) : System.Windows.Rect.Empty; } }
        public IRawElementProviderSimple[] GetEmbeddedFragmentRoots() { return null; }
        public void SetFocus() { Select(); o.Focus(); }
        public IRawElementProviderFragmentRoot FragmentRoot { get { return root; } }
        public void Select() { if (i < o.Items.Count) o.SelectedIndex = i; }
        public void AddToSelection() { Select(); }
        public void RemoveFromSelection() { if (o.SelectedIndex == i) o.SelectedIndex = -1; }
        public bool IsSelected { get { return o.SelectedIndex == i; } }
        public IRawElementProviderSimple SelectionContainer { get { return root; } }
    }
}
