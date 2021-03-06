﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using dnEditor.Forms;
using dnEditor.Misc;
using dnlib.DotNet;
using dnlib.Utils;

namespace dnEditor.Handlers
{
    public class TreeViewHandler : ITreeMenu
    {
        private TreeNode _currentSearchNode;
        private List<object> _searchPath;
        private TreeNode _currentModule;

        public Form CurrentForm;
        public TreeNode CurrentMethod;   
        public ContextMenuStrip CurrentTreeMenu;
        public TreeView CurrentTreeView;
        public List<string> NameSpaceList = new List<string>();
        public ToolTip NodeToolTip = new ToolTip();
        public TreeNode RefNode;
        public TreeNode SelectedNode;
        public NavigationHistory NavigationHistory;

        public TreeNode CurrentModule
        {
            get { return _currentModule; }
            set
            {
                if (value != _currentModule || value == null)
                    NavigationHistory.Clear();

                _currentModule = value;
            }
        }

        public TreeViewHandler(TreeView currentTreeView, ContextMenuStrip currentTreeMenu)
        {
            CurrentForm = currentTreeView.FindForm();
            CurrentTreeView = currentTreeView;
            CurrentTreeMenu = currentTreeMenu;
            NavigationHistory = new NavigationHistory(this);
        }

        public void BrowseAndExpandMember(object member)
        {
            if (member == null)
                return;

            var memberPath = new MemberPath(member);

            if (memberPath.Path.Count == 0)
                return;

            MainForm.HandleExpand = false;
            _searchPath = memberPath.Path;

            TreeNode node = _currentModule;

            while (node != null)
            {
                node.Expand();
                node = node.Parent;
            }

            var mainType = memberPath.Path[0] as TypeDef;
            _currentSearchNode = _currentModule.Nodes.Cast<TreeNode>().First(n => n.Text == mainType.Namespace);
            _currentSearchNode.Expand();

            BrowseAndExpandMember();
        }

        private void BrowseAndExpandMember()
        {
            object member = _searchPath.FirstOrDefault();

            if (member == null)
            {
                CurrentTreeView.Focus();
                _currentSearchNode.EnsureVisible();
                CurrentTreeView.SelectedNode = _currentSearchNode;

                MainForm.HandleExpand = true;
                return;
            }

            if (member is TypeDef)
            {
                _currentSearchNode =
                   _currentSearchNode.Nodes.Cast<TreeNode>().First(n => n.Text == NewType(member as TypeDef).Text);

                _currentSearchNode.Expand();
            }
            else if (member is MethodDef)
            {
                var method = (MethodDef) member;

                TreeNode newNode = _currentSearchNode.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.Text == NewMethod(method).Text);

                if (newNode != null)
                    _currentSearchNode = newNode;
                else
                {
                    foreach (TreeNode node in _currentSearchNode.Nodes.Cast<TreeNode>().Where(n => n.Tag is PropertyDef || n.Tag is EventDef))
                    {
                        newNode = node.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.Text == NewMethod(method).Text);

                        if (newNode != null)
                        {
                            _currentSearchNode = newNode;
                            break;
                        }
                    }

                    if (newNode == null)
                    {
                        _searchPath.Clear();
                        BrowseAndExpandMember();
                    }
                }

                treeView_NodeMouseClick(this,
                    new TreeNodeMouseClickEventArgs(_currentSearchNode, MouseButtons.Left, 1,
                        _currentSearchNode.Bounds.X, _currentSearchNode.Bounds.Y));

                DataGridViewHandler.SelectTab();
            }
            else if (member is FieldDef)
            {
                _currentSearchNode =
                    _currentSearchNode.Nodes.Cast<TreeNode>().First(n => n.Text == NewField(member as FieldDef).Text);

                treeView_NodeMouseClick(this,
                    new TreeNodeMouseClickEventArgs(_currentSearchNode, MouseButtons.Left, 1,
                        _currentSearchNode.Bounds.X, _currentSearchNode.Bounds.Y));

                DataGridViewHandler.SelectTab();
            }
            else if (member is PropertyDef)
            {
                _currentSearchNode =
                    _currentSearchNode.Nodes.Cast<TreeNode>().First(n => n.Text == NewProperty(member as PropertyDef).Text);

                _currentSearchNode.Expand();
            }
            else if (member is EventDef)
            {
                _currentSearchNode =
                    _currentSearchNode.Nodes.Cast<TreeNode>().First(n => n.Text == NewEvent(member as EventDef).Text);

                _currentSearchNode.Expand();
            }

            _searchPath.RemoveAt(0);

            if (_currentSearchNode.HasVirtualNode())
            {
                var processor = new NodeDevirtualizer(_currentSearchNode, this);
                processor.WorkerFinished += searchProcessor_WorkerFinished;
                processor.ProcessNode();
                VirtualNodeHandler.VirtualNodes--;
                _currentSearchNode.Nodes.Remove(_currentSearchNode.FindVirtualNode());
            }
            else
            {
                BrowseAndExpandMember();
            }
        }

        private void searchProcessor_WorkerFinished(TreeNode processedNode)
        {
            _currentSearchNode = processedNode;
            BrowseAndExpandMember();
        }

        private void OrderNamespaces()
        {
            var nodes = new List<TreeNode>();

            nodes.AddRange(_currentModule.Nodes.Cast<TreeNode>().Where(n => !(n.Tag is string)));
            nodes.AddRange(
                _currentModule.Nodes.Cast<TreeNode>().Where(n => n.Tag is string).OrderBy(n => n.Text.ToLower()));
            _currentModule.Nodes.Clear();
            _currentModule.Nodes.AddRange(nodes.ToArray());
        }

        private void OrderReferences()
        {
            var nodes = new List<TreeNode>();

            TreeNode referenceFolder = _currentModule.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.Tag is AssemblyRef[]);
            if (referenceFolder == null) return;

            nodes.AddRange(referenceFolder.Nodes.Cast<TreeNode>().OrderBy(n => n.Text.ToLower()));

            referenceFolder.Nodes.Clear();
            referenceFolder.Nodes.AddRange(nodes.ToArray());
        }

        public TreeNode NewNode(string text, bool shorten = true)
        {
            var node = new TreeNode(shorten ? text.ShortenTreeNodeText() : text);
            node.Name = text;
            node.ContextMenuStrip = CurrentTreeMenu;

            return node;
        }

        public TreeNode NewFile(ModuleDefMD file, string path)
        {
            if (String.IsNullOrEmpty(path))
                throw new ArgumentException("Path is invalid!");

            TreeNode node = NewNode(file.Name, false);
            node.Tag = file;
            node.ImageIndex = node.SelectedImageIndex = 0;
            node.ToolTipText = path;

            return node;
        }

        public TreeNode NewModule(ModuleDefMD module)
        {
            TreeNode node = NewNode(module.FullName, false);
            node.Tag = module;
            node.ImageIndex = node.SelectedImageIndex = 28;

            return node;
        }

        public TreeNode NewReferenceFolder()
        {
            TreeNode node = NewNode("References", false);
            node.ImageIndex = node.SelectedImageIndex = 44;

            return node;
        }

        public TreeNode NewAssemblyRef(AssemblyRef assemblyRef)
        {
            TreeNode node = NewNode(assemblyRef.FullName, false);
            node.Tag = assemblyRef;
            node.ImageIndex = node.SelectedImageIndex = 0;

            return node;
        }

        public TreeNode NewNameSpace(string nameSpace)
        {
            TreeNode node = NewNode(nameSpace, false);
            node.Tag = nameSpace;
            node.ImageIndex = node.SelectedImageIndex = 31;

            return node;
        }

        public TreeNode NewType(TypeDef type) // (Class)
        {
            TreeNode node = NewNode(type.GetExtendedName());
            node.Tag = type;
            node.ImageIndex = node.SelectedImageIndex = 6;

            return node;
        }

        public TreeNode NewMethod(MethodDef method)
        {
            TreeNode node = NewNode(method.GetExtendedName(true));
            node.Tag = method;
            node.ImageIndex = node.SelectedImageIndex = 30;

            return node;
        }

        public TreeNode NewProperty(PropertyDef property)
        {
            TreeNode node = NewNode(String.Format(property.Name));

            node.Tag = property;
            node.ImageIndex = node.SelectedImageIndex = 43;

            if (property.GetMethod != null)
            {
                string type = property.GetMethod.ReturnType.GetExtendedName();

                node.Nodes.Add(NewMethod(property.GetMethod));
                node.Text = String.Format("{0}: {1}", property.Name, type);
            }

            if (property.SetMethod != null)
            {
                node.Nodes.Add(NewMethod(property.SetMethod));
            }

            foreach (MethodDef method in property.OtherMethods)
            {
                node.Nodes.Add(NewMethod(method));
            }

            return node;
        }

        public TreeNode NewEvent(EventDef @event)
        {
            TreeNode node = NewNode(String.Format("{0}: {1}", @event.Name, "EventHandler"));

            node.Tag = @event;
            node.ImageIndex = node.SelectedImageIndex = 15;

            if (@event.AddMethod != null)
            {
                node.Nodes.Add(NewMethod(@event.AddMethod));
            }

            if (@event.RemoveMethod != null)
            {
                node.Nodes.Add(NewMethod(@event.RemoveMethod));
            }

            if (@event.InvokeMethod != null)
            {
                node.Nodes.Add(NewMethod(@event.InvokeMethod));
            }

            foreach (MethodDef method in @event.OtherMethods)
            {
                node.Nodes.Add(NewMethod(method));
            }

            return node;
        }

        public TreeNode NewField(FieldDef field)
        {
            string type = field.FieldType.GetExtendedName();

            TreeNode node = NewNode(String.Format("{0}: {1}", field.Name, type));
            node.Tag = field;
            node.ImageIndex = node.SelectedImageIndex = 17;

            return node;
        }

        #region Interface

        public string DragDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return null;
            }
            var filePath = (string[]) (e.Data.GetData(DataFormats.FileDrop));
            string fileLoc = filePath.FirstOrDefault(File.Exists);
            if (fileLoc == null) return null;

            return Path.GetFullPath(fileLoc);
        }

        public void DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Copy;
        }

        #endregion Interface

        #region TreeMenuStrip

        public void treeMenu_Opened(object sender, EventArgs e)
        {
            CurrentTreeMenu.Items.Cast<ToolStripItem>().First(i => i.Text == "Collapse all").Enabled = false;
            CurrentTreeMenu.Items.Cast<ToolStripItem>().First(i => i.Text == "Close assembly").Enabled = false;

            if (SelectedNode != null)
            {
                if (SelectedNode.Nodes.Count > 0)
                    CurrentTreeMenu.Items.Cast<ToolStripItem>().First(i => i.Text == "Collapse all").Enabled = true;

                if (SelectedNode.Tag is ModuleDefMD)
                    CurrentTreeMenu.Items.Cast<ToolStripItem>().First(i => i.Text == "Close assembly").Enabled = true;
            }
        }

        public void goToEntryPointToolStripMenuItem_Click(object sender, EventArgs e)
        {
            BrowseAndExpandMember((_currentModule.Tag as ModuleDefMD).EntryPoint);
        }

        public void goToModuleCCtorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            BrowseAndExpandMember((_currentModule.Tag as ModuleDefMD).GlobalType.FindStaticConstructor());
        }

        public void expandToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedNode != null)
                SelectedNode.Expand();
        }

        public void collapseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedNode != null)
                SelectedNode.Collapse();
        }

        public void collapseAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedNode == null)
                CurrentTreeView.CollapseAll();
            else
                SelectedNode.Collapse(false);
        }

        public void closeToolStripMenuItem_Click(object sender, EventArgs e)
        {
        }

        public void closeToolStripMenuItem_Click(object sender, EventArgs e, ref CurrentAssembly currentAssembly)
        {
            //TODO: Update on changes

            if (SelectedNode == null) return;

            TreeNode assembly = SelectedNode.FirstParentNode();
            if (assembly == null) return;

            CurrentMethod = null;
            CurrentModule = null;
            currentAssembly = null;
            DataGridViewHandler.ClearInstructions();
            VariableHandler.ClearVariables();
            ILSpyHandler.Clear();
            AnalysisHandler.Reset();
            assembly.Remove();
        }

        #endregion TreeMenuStrip

        #region TreeView Events

        public void LoadAssembly(ModuleDefMD manifestModule, string path, bool clear)
        {
            AnalysisHandler.Reset();
            RefNode = null;
            CurrentModule = null;

            if (clear)
                CurrentTreeView.Nodes.Clear();

            TreeNode file = NewFile(manifestModule, path);
            file.AddTo(CurrentTreeView);

            ModuleDefMD module = manifestModule;
            NameSpaceList.Clear();

            TreeNode moduleNode = NewModule(module);

            moduleNode.AddTo(file);

            CurrentModule = moduleNode;

            if (module.Types.Any())
            {
                foreach (TypeDef type in module.Types.OrderBy(t => t.Name.ToLower()))
                {
                    new TypeHandler(this).HandleType(type, false);
                }
            }

            CurrentModule = moduleNode;

            if (module.GetAssemblyRefs().Any())
            {
                new ReferenceHandler(this).HandleReferences(module.GetAssemblyRefs());
            }

            var processor = new NodeDevirtualizer(RefNode, this);
            processor.ProcessNode();
            processor.WorkerFinished += processor_WorkerFinished;
            RefNode = processor.Node;
        }

        private void processor_WorkerFinished(TreeNode processedNode)
        {
            OrderNamespaces();
            OrderReferences();
        }

        public void treeView1_AfterExpand(object sender, TreeViewEventArgs e)
        {
            VirtualNodeUtilities.ExpandHandler(e.Node, this);
        }

        public void treeView_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            TreeNode assemblyNode = e.Node.FirstParentNode();

            if (MainForm.CurrentAssembly == null || MainForm.CurrentAssembly.ManifestModule != assemblyNode.Tag as ModuleDefMD)
            {
                AnalysisHandler.Reset();

                MainForm.CurrentAssembly = new CurrentAssembly(assemblyNode.Tag as ModuleDefMD);
                MainForm.CurrentAssembly.Path = assemblyNode.ToolTipText;

                if (e.Node.ModuleNode() != null)
                    CurrentModule = e.Node.ModuleNode();
            }

            if (e.Node.Tag is MethodDef)
            {
                var method = e.Node.Tag as MethodDef;

                if (CurrentMethod == null || method != CurrentMethod.Tag as MethodDef)
                {
                    ILSpyHandler.Clear();
                    DataGridViewHandler.ReadMethod(method);
                    CurrentMethod = e.Node;

                    NavigationHistory.AddPastHistory(method);
                }
            }
            else
            {
                CurrentMethod = null;
                MainForm.CurrentAssembly.Method = null;
                DataGridViewHandler.ClearInstructions();
                VariableHandler.ClearVariables();
                ILSpyHandler.Clear();
            }

            SelectedNode = e.Node;
        }

        public void treeView_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e,
            ref CurrentAssembly currentAssembly)
        {
            if (!(e.Node.Tag is AssemblyRef) || e.Node.TreeView.SelectedNode != e.Node)
                return;

            var assemblyRef = e.Node.Tag as AssemblyRef;
            string runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
            string directory = Directory.GetParent(currentAssembly.Path).FullName;

            var paths = new List<string>
            {Path.Combine(directory, assemblyRef.Name + ".dll"), Path.Combine(directory, assemblyRef.Name + ".exe")};

            var paths2 = new List<string>
            {
                Path.Combine(runtimeDirectory, assemblyRef.Name + ".exe"),
                Path.Combine(runtimeDirectory, assemblyRef.Name + ".dll")
            };


            if (paths.Where(File.Exists).Count() == 1)
            {
                Functions.OpenFile(this, paths.First(File.Exists), ref currentAssembly);
                return;
            }
            if (paths2.Where(File.Exists).Count() == 1)
            {
                Functions.OpenFile(this, paths2.First(File.Exists), ref currentAssembly);
                return;
            }

            if (
                MessageBox.Show("Could not automatically find reference file. Browse for it?", "Error",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                currentAssembly = null;
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = String.Format("Browse for the reference \"{0}\"", assemblyRef.Name),
                Filter = "Executable Files (*.exe)|*.exe|Library Files (*.dll)|*.dll"
            };

            if (dialog.ShowDialog() != DialogResult.OK && File.Exists(dialog.FileName))
            {
                currentAssembly = null;
                return;
            }

            Functions.OpenFile(this, dialog.FileName, ref currentAssembly);
        }

        public void treeView1_NodeMouseHover(object sender, TreeNodeMouseHoverEventArgs e)
        {
            TreeNode node = e.Node;

            if (node == null ||
                (!(node.Tag is ModuleDefMD) && !(node.Tag is TypeDef) && !(node.Tag is MethodDef) &&
                 !(node.Tag is PropertyDef) && !(node.Tag is FieldDef) && !(node.Tag is EventDef)) ||
                node.ToolTipText != string.Empty)
            {
                return;
            }

            NodeToolTip.Active = false;
            NodeToolTip.Dispose();
            NodeToolTip = new ToolTip();

            if (node.Tag is ModuleDefMD)
            {
                var module = node.Tag as ModuleDefMD;
                string text = string.Format(
@"MDToken: 0x{0}
RID: {1}
FullName: {2}", module.MDToken.FullMetadataTokenString(), module.MDToken.Rid, module.FullName);

                node.ToolTipText = text;
                NodeToolTip.Show(text, CurrentTreeView);
            }

            else if (node.Tag is TypeDef)
            {
                var type = node.Tag as TypeDef;
                string text = string.Format(
@"MDToken: 0x{0}
RID: {1}
FullName: {2}", type.MDToken.FullMetadataTokenString(), type.MDToken.Rid, type.FullName);

                node.ToolTipText = text;
                NodeToolTip.Show(text, CurrentTreeView);
            }

            else if (node.Tag is MethodDef)
            {
                var method = node.Tag as MethodDef;
                string text = string.Format(
@"MDToken: 0x{0}
RID: {1}
FullName: {2}", method.MDToken.FullMetadataTokenString(), method.MDToken.Rid, method.FullName);

                node.ToolTipText = text;
                NodeToolTip.Show(text, CurrentTreeView);
            }

            else if (node.Tag is PropertyDef)
            {
                var property = node.Tag as PropertyDef;
                string text = string.Format(
@"MDToken: 0x{0}
RID: {1}
FullName: {2}", property.MDToken.FullMetadataTokenString(), property.MDToken.Rid, property.FullName);

                node.ToolTipText = text;
                NodeToolTip.Show(text, CurrentTreeView);
            }

            else if (node.Tag is FieldDef)
            {
                var field = node.Tag as FieldDef;
                string text = string.Format(
@"MDToken: 0x{0}
RID: {1}
FullName: {2}", field.MDToken.FullMetadataTokenString(), field.MDToken.Rid, field.FullName);

                node.ToolTipText = text;
                NodeToolTip.Show(text, CurrentTreeView);
            }

            else if (node.Tag is EventDef)
            {
                var @event = node.Tag as EventDef;
                string text = string.Format(
@"MDToken: 0x{0}
RID: {1}
FullName: {2}", @event.MDToken.FullMetadataTokenString(), @event.MDToken.Rid, @event.FullName);

                node.ToolTipText = text;
                NodeToolTip.Show(text, CurrentTreeView);
            }
        }

        #endregion TreeView Events
    }

    public static class NodeEmitter
    {
        public static void AddTo(this TreeNode node, TreeView view)
        {
            view.Nodes.Add(node);
        }

        public static void AddTo(this TreeNode node, TreeNode parentNode)
        {
            parentNode.Nodes.Add(node);
        }
    }
}