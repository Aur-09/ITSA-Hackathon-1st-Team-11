using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TrackBar;

namespace Kovsie_Study_and_Assignment_Tracker
{
    public partial class frmStudyAndAssignement : Form
    {
        private const string SearchPlaceholder = "Search by module or keyword";

        private readonly List<Assignment> assignments = new List<Assignment>();
        private readonly List<string> modules = new List<string>();

        // Tracks exactly what is currently shown in lstCurrentAssignments, in display order,
        // so a selected row can be mapped back to the correct Assignment safely (instead of
        // re-parsing the displayed text, which breaks if two rows ever look identical).
        private readonly List<Assignment> displayOrder = new List<Assignment>();

        private readonly string dataFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KovsieStudyAndAssignmentTracker",
            "assignments.json");
        private readonly string modulesFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KovsieStudyAndAssignmentTracker",
            "modules.txt");

        private string sortField = "DueDate";
        private bool sortAscending = true;
        private bool showingOverdueOnly = false;

        // Every control (position, size, colour, anchor) is declared and configured in
        // Study And Assignment Tracker.Designer.cs now, exactly as Visual Studio's own
        // designer would generate it - what you see there is what runs. This file only
        // holds behaviour: data, validation, and the handful of custom paint routines
        // that inherently can't be a static design-time property (gradients, list rows).
        private readonly List<Assignment> studyPlanOrder = new List<Assignment>();

        public frmStudyAndAssignement()
        {
            InitializeComponent();
            ApplyBackgroundArt();
            WireEvents();
            LoadData();
            RefreshModuleList();
            RefreshAssignmentList();
            countdownTimer.Start();
        }

        // Loads the two generated background images from the Assets folder next to the .exe
        // and applies them: the abstract "hero_bg" artwork behind the two gradient header
        // panels (which now paint themselves semi-transparently, see pnlHeroHeader_Paint /
        // pnlCountdownHero_Paint), and the light dot-grid "paper_texture" tiled behind each
        // tab page, so every screen has a subtle branded backdrop instead of a flat colour.
        // Wrapped in try/catch so a missing or corrupt asset file never crashes the app -
        // worst case, it just falls back to the plain colours that shipped before this.
        private void ApplyBackgroundArt()
        {
            System.Drawing.Image heroArt = LoadAppImage("hero_bg.png");
            System.Drawing.Image paperTexture = LoadAppImage("paper_texture.png");

            if (heroArt != null)
            {
                pnlHeroHeader.BackgroundImage = heroArt;
                pnlHeroHeader.BackgroundImageLayout = ImageLayout.Stretch;
                pnlCountdownHero.BackgroundImage = heroArt;
                pnlCountdownHero.BackgroundImageLayout = ImageLayout.Stretch;
            }

            if (paperTexture != null)
            {
                this.BackgroundImage = paperTexture;
                this.BackgroundImageLayout = ImageLayout.Tile;

                tbcAdd_Remove_Modules.BackgroundImage = paperTexture;
                tbcAdd_Remove_Modules.BackgroundImageLayout = ImageLayout.Tile;

                tbcAssignements.BackgroundImage = paperTexture;
                tbcAssignements.BackgroundImageLayout = ImageLayout.Tile;

                tbcStudySchedule.BackgroundImage = paperTexture;
                tbcStudySchedule.BackgroundImageLayout = ImageLayout.Tile;
            }
        }

        private static System.Drawing.Image LoadAppImage(string fileName)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", fileName);
                if (!File.Exists(path)) return null;

                // Load through a MemoryStream rather than Image.FromFile so the file itself
                // isn't left open/locked for the lifetime of the app.
                byte[] bytes = File.ReadAllBytes(path);
                using (MemoryStream stream = new MemoryStream(bytes))
                {
                    return System.Drawing.Image.FromStream(stream);
                }
            }
            catch
            {
                return null;
            }
        }
        // THis event handler Paints the gradient fill for pnlHeroHeader. The panel itself, its position and
        // its title label are all declared in the Designer file - this is the one piece
        // (a gradient) that has no Designer property, so it has to stay code.
        private void pnlHeroHeader_Paint(object sender, PaintEventArgs e)
        {
            // The panel's BackgroundImage (set in ApplyBackgroundArt) is drawn by WinForms
            // before this handler runs. The gradient below is now semi-transparent (alpha 205
            // instead of fully opaque) so that artwork shows through underneath the brand tint,
            // instead of being covered up by it.
            using (System.Drawing.Drawing2D.LinearGradientBrush brush =
                new System.Drawing.Drawing2D.LinearGradientBrush(
                    pnlHeroHeader.ClientRectangle,
                    System.Drawing.Color.FromArgb(205, 72, 61, 196),
                    System.Drawing.Color.FromArgb(205, 48, 112, 190),
                    System.Drawing.Drawing2D.LinearGradientMode.Horizontal))
            {
                e.Graphics.FillRectangle(brush, pnlHeroHeader.ClientRectangle);
            }
        }

        private void WireEvents()
        {
            btnAddModule.Click += btnAddModule_Click;
            btnRemoveModule.Click += btnRemoveModule_Click;
            btnViewDetails.Click += btnViewDetails_Click;
            FormClosing += frmStudyAndAssignement_FormClosing;
        }

        //Wired to countdownTimer.Tick in the Designer file - ticks every second and just forwards to the same refresh logic used after any data change.
        private void countdownTimer_Tick(object sender, EventArgs e)
        {
            UpdateCountdown();
        }

        private void pnlCountdownHero_Paint(object sender, PaintEventArgs e)
        {
            Assignment next = studyPlanOrder.FirstOrDefault();
            // Alpha 205 (instead of fully opaque) lets the panel's BackgroundImage - the same
            // abstract artwork used on pnlHeroHeader, set in ApplyBackgroundArt - show through
            // under whichever urgency tint applies below.
            System.Drawing.Color from = System.Drawing.Color.FromArgb(205, 72, 61, 196);
            System.Drawing.Color to = System.Drawing.Color.FromArgb(205, 48, 112, 190);

            if (next != null)
            {
                if (next.IsOverdue)
                {
                    from = System.Drawing.Color.FromArgb(205, 196, 60, 60);
                    to = System.Drawing.Color.FromArgb(205, 214, 100, 65);
                }
                else if (next.DaysRemaining <= 2)
                {
                    from = System.Drawing.Color.FromArgb(205, 214, 130, 40);
                    to = System.Drawing.Color.FromArgb(205, 230, 170, 60);
                }
            }

            using (System.Drawing.Drawing2D.LinearGradientBrush brush =
                new System.Drawing.Drawing2D.LinearGradientBrush(
                    pnlCountdownHero.ClientRectangle, from, to,
                    System.Drawing.Drawing2D.LinearGradientMode.Horizontal))
            {
                e.Graphics.FillRectangle(brush, pnlCountdownHero.ClientRectangle);
            }
        }

        // Refreshes the countdown card. Called once a second by countdownTimer, and
        // also straight after any add/edit/delete/complete so it never shows stale data.
        // "End of the due day" (23:59:59) is used as the deadline instant, matching
        // Assignment.IsOverdue - which only flags a day as overdue once it has fully
        // passed, not the moment it starts.
        private void UpdateCountdown()
        {
            Assignment next = studyPlanOrder.FirstOrDefault();

            if (next == null)
            {
                lblCountdownCaption.Text = "ALL CLEAR";
                lblCountdownTitle.Text = "Nothing outstanding - nice work staying ahead!";
                lblCountdownTime.Text = string.Empty;
                pnlCountdownHero.Invalidate();
                return;
            }

            lblCountdownCaption.Text = "NEXT UP";
            lblCountdownTitle.Text = string.Format("{0}  -  {1}", next.Module, next.Title);

            DateTime deadline = next.DueDate.Date.AddDays(1).AddSeconds(-1);
            TimeSpan remaining = deadline - DateTime.Now;

            if (remaining.TotalSeconds <= 0)
            {
                TimeSpan overdueBy = DateTime.Now - deadline;
                lblCountdownTime.Text = string.Format("Overdue {0}d {1}h", overdueBy.Days, overdueBy.Hours);
            }
            else if (remaining.TotalDays >= 1)
            {
                lblCountdownTime.Text = string.Format("{0}d {1}h {2}m", remaining.Days, remaining.Hours, remaining.Minutes);
            }
            else
            {
                lblCountdownTime.Text = string.Format("{0}h {1}m {2}s", remaining.Hours, remaining.Minutes, remaining.Seconds);
            }

            pnlCountdownHero.Invalidate();
        }

        // Rebuilds the priority queue: everything not yet completed, oldest due date
        // first (so overdue work - which has the earliest due dates - naturally sits
        // at the top). Also repaints the heatmap, since both reflect the same data.
        private void RefreshStudyPlan()
        {
            studyPlanOrder.Clear();
            studyPlanOrder.AddRange(assignments.Where(a => !a.IsCompleted).OrderBy(a => a.DueDate));

            lstStudyPlan.Items.Clear();
            for (int i = 0; i < studyPlanOrder.Count; i++)
            {
                lstStudyPlan.Items.Add(i);
            }

            if (studyPlanOrder.Count == 0)
            {
                lstStudyPlan.Items.Add(-1);
            }

            UpdateCountdown();
        }

        private void lstStudyPlan_DrawItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();

            if (studyPlanOrder.Count == 0)
            {
                using (System.Drawing.SolidBrush textBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Gray))
                {
                    e.Graphics.DrawString("Nothing outstanding - nice work staying ahead!", e.Font, textBrush,
                        new System.Drawing.Rectangle(e.Bounds.Left + 10, e.Bounds.Top, e.Bounds.Width - 10, e.Bounds.Height),
                        new System.Drawing.StringFormat { LineAlignment = System.Drawing.StringAlignment.Center });
                }
                return;
            }

            if (e.Index < 0 || e.Index >= studyPlanOrder.Count)
            {
                return;
            }

            Assignment a = studyPlanOrder[e.Index];
            System.Drawing.Color urgencyColor = GetUrgencyColor(a);

            System.Drawing.Rectangle stripe =
                new System.Drawing.Rectangle(e.Bounds.Left + 4, e.Bounds.Top + 3, 10, e.Bounds.Height - 6);
            using (System.Drawing.SolidBrush stripeBrush = new System.Drawing.SolidBrush(urgencyColor))
            {
                e.Graphics.FillRectangle(stripeBrush, stripe);
            }

            string text = string.Format("{0}  |  {1}  ({2:dddd, dd MMM})  -  {3}",
                a.Module, a.Title, a.DueDate, a.StatusText);
            System.Drawing.Rectangle textBounds = new System.Drawing.Rectangle(
                e.Bounds.Left + 24, e.Bounds.Top, e.Bounds.Width - 28, e.Bounds.Height);
            using (System.Drawing.SolidBrush textBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Black))
            {
                e.Graphics.DrawString(text, e.Font, textBrush, textBounds,
                    new System.Drawing.StringFormat { LineAlignment = System.Drawing.StringAlignment.Center });
            }
            e.DrawFocusRectangle();
        }

        private System.Drawing.Color GetUrgencyColor(Assignment a)
        {
            if (a.IsOverdue)
            {
                return System.Drawing.Color.FromArgb(214, 69, 65);
            }
            if (a.DaysRemaining <= 2)
            {
                return System.Drawing.Color.FromArgb(255, 165, 90);
            }
            if (a.DaysRemaining <= 7)
            {
                return System.Drawing.Color.FromArgb(230, 190, 40);
            }
            return System.Drawing.Color.FromArgb(60, 170, 120);
        }

        private void LoadData()
        {
            string directory = Path.GetDirectoryName(dataFilePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(dataFilePath))
            {
                try
                {
                    using (FileStream stream = File.OpenRead(dataFilePath))
                    {
                        DataContractJsonSerializer serializer =
                            new DataContractJsonSerializer(typeof(List<Assignment>));
                        List<Assignment> savedAssignments = serializer.ReadObject(stream) as List<Assignment>;
                        if (savedAssignments != null)
                        {
                            assignments.AddRange(savedAssignments);
                            foreach (Assignment assignment in savedAssignments)
                            {
                                if (!string.IsNullOrWhiteSpace(assignment.Module) && !modules.Contains(assignment.Module))
                                {
                                    modules.Add(assignment.Module);
                                }
                            }

                            if (File.Exists(modulesFilePath))
                            {
                                foreach (string module in File.ReadAllLines(modulesFilePath))
                                {
                                    string trimmedModule = module.Trim();
                                    if (!string.IsNullOrWhiteSpace(trimmedModule) && !modules.Contains(trimmedModule))
                                    {
                                        modules.Add(trimmedModule);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Saved data could not be loaded: " + ex.Message,
                        "Load Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            if (assignments.Count == 0)
            {
                modules.AddRange(new[] { "MATH1234", "PHYS2048", "HIST1101" });
                assignments.Add(new Assignment("MATH1234", "Math Assignment 1", DateTime.Today.AddDays(-2)));
                assignments.Add(new Assignment("PHYS2048", "Physics Test Prep", DateTime.Today.AddDays(2)));
                assignments.Add(new Assignment("HIST1101", "History Essay", DateTime.Today.AddDays(5)));
            }
        }

        private void SaveData()
        {
            string directory = Path.GetDirectoryName(dataFilePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (FileStream stream = File.Create(dataFilePath))
            {
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(typeof(List<Assignment>));
                serializer.WriteObject(stream, assignments);
            }

            File.WriteAllLines(modulesFilePath, modules.OrderBy(module => module));
        }

        private void RefreshModuleList()
        {
            lstModules_remove.Items.Clear();
            lstModules_ViewDetails.Items.Clear();
            cmbAssignmentModule.Items.Clear();

            foreach (string module in modules.OrderBy(m => m))
            {
                lstModules_remove.Items.Add(module);
                lstModules_ViewDetails.Items.Add(module);
                cmbAssignmentModule.Items.Add(module);
            }

            if (cmbAssignmentModule.Items.Count > 0)
            {
                cmbAssignmentModule.SelectedIndex = 0;
            }
        }

        private Assignment GetSelectedAssignment()
        {
            int index = lstCurrentAssignments.SelectedIndex;
            if (index < 0 || index >= displayOrder.Count)
            {
                return null;
            }
            return displayOrder[index];
        }

        private string GetActiveSearchTerm()
        {
            if (txtSearch == null || txtSearch.Text == SearchPlaceholder)
            {
                return string.Empty;
            }
            return txtSearch.Text.Trim();
        }

        private IEnumerable<Assignment> SortAssignments(IEnumerable<Assignment> source)
        {
            switch (sortField)
            {
                case "Module":
                    return sortAscending
                        ? source.OrderBy(a => a.Module).ThenBy(a => a.DueDate)
                        : source.OrderByDescending(a => a.Module).ThenBy(a => a.DueDate);
                case "Title":
                    return sortAscending
                        ? source.OrderBy(a => a.Title)
                        : source.OrderByDescending(a => a.Title);
                default:
                    return sortAscending
                        ? source.OrderBy(a => a.DueDate)
                        : source.OrderByDescending(a => a.DueDate);
            }
        }

        private IEnumerable<Assignment> GetVisibleAssignments()
        {
            IEnumerable<Assignment> visible = assignments;

            if (showingOverdueOnly)
            {
                visible = visible.Where(a => a.IsOverdue);
            }

            string searchTerm = GetActiveSearchTerm();
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                visible = visible.Where(a =>
                    a.Module.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    a.Title.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            return SortAssignments(visible);
        }

        private void RenderAssignments(IEnumerable<Assignment> visibleAssignments)
        {
            displayOrder.Clear();
            displayOrder.AddRange(visibleAssignments);

            lstCurrentAssignments.Items.Clear();
            foreach (Assignment assignment in displayOrder)
            {
                lstCurrentAssignments.Items.Add(assignment.ToString());
            }

            UpdateSummary();
        }

        private void RefreshAssignmentList()
        {
            RenderAssignments(GetVisibleAssignments());
        }

        private void UpdateSummary()
        {
            lblTotalAssignments.Text = "Total assignments: " + assignments.Count;
            lblOverdueAssignments.Text = "Overdue: " + assignments.Count(a => a.IsOverdue);
            lblModulesTracked.Text = "Modules tracked: " + modules.Count;
            txtModuleSummary.Text = BuildModuleSummaryText();
            RefreshStudyPlan();
        }

        // Builds a per-module breakdown (total and overdue counts) by iterating over the collection.
        // Demonstrates meaningful string building for the basic summary/report requirement.

        private string BuildModuleSummaryText()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Assignments per module:");
            builder.AppendLine();

            if (modules.Count == 0)
            {
                builder.AppendLine("No modules added yet.");
                return builder.ToString();
            }

            foreach (string module in modules.OrderBy(m => m))
            {
                int total = assignments.Count(a => a.Module == module);
                int overdue = assignments.Count(a => a.Module == module && a.IsOverdue);
                builder.AppendFormat("{0}: {1} total, {2} overdue", module, total, overdue);
                builder.AppendLine();
            }

            return builder.ToString();
        }

        private bool IsValidModuleCode(string code)
        {
            return Regex.IsMatch(code, "^[A-Z]{4}[0-9]{4}$");
        }

        // Validates the module/title fields shared by both the Add and Edit forms.
        // Returns false and an error message instead of letting bad input crash the app

        private bool TryValidateAssignmentInput(object selectedModule, string title, out string errorMessage)
        {
            if (selectedModule == null)
            {
                errorMessage = "Please select a module before continuing.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(title))
            {
                errorMessage = "Please enter an assignment title.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        private System.Drawing.Color GetStatusColor(Assignment assignment)
        {
            if (assignment.IsCompleted)
            {
                return System.Drawing.Color.SeaGreen;
            }
            if (assignment.IsOverdue)
            {
                return System.Drawing.Color.Firebrick;
            }
            return System.Drawing.Color.Black;
        }

        private string GetSortButtonText()
        {
            string direction = sortAscending ? "A-Z / earliest" : "Z-A / latest";
            return string.Format("Sort: {0} ({1})", sortField, direction);
        }

        private void CycleSortOption()
        {
            if (sortField == "DueDate" && sortAscending)
            {
                sortAscending = false;
            }
            else if (sortField == "DueDate")
            {
                sortField = "Module";
                sortAscending = true;
            }
            else if (sortField == "Module")
            {
                sortField = "Title";
                sortAscending = true;
            }
            else
            {
                sortField = "DueDate";
                sortAscending = true;
            }

            btnSortAssignments.Text = GetSortButtonText();
        }

        private void btnAddModule_Click(object sender, EventArgs e)
        {
            string moduleCode = txtModules.Text.Trim().ToUpperInvariant();
            if (!IsValidModuleCode(moduleCode))
            {
                MessageBox.Show("Module code must be in the format ABCD1234.",
                    "Invalid Module", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (modules.Contains(moduleCode))
            {
                MessageBox.Show("This module is already on your list.",
                    "Duplicate Module", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            modules.Add(moduleCode);
            RefreshModuleList();
            UpdateSummary();
            txtModules.Clear();
            SaveData();
        }

        private void btnRemoveModule_Click(object sender, EventArgs e)
        {
            if (lstModules_remove.SelectedItem == null)
            {
                MessageBox.Show("Select a module to remove.", "Remove Module",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string selectedModule = lstModules_remove.SelectedItem.ToString();
            if (assignments.Any(a => a.Module == selectedModule))
            {
                MessageBox.Show("Remove or update assignments for this module first.",
                    "Module In Use", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            modules.Remove(selectedModule);
            RefreshModuleList();
            UpdateSummary();
            SaveData();
        }

        private void btnViewDetails_Click(object sender, EventArgs e)
        {
            if (lstModules_ViewDetails.SelectedItem == null)
            {
                MessageBox.Show("Select a module to view.", "View Module",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string module = lstModules_ViewDetails.SelectedItem.ToString();
            int total = assignments.Count(a => a.Module == module);
            int overdue = assignments.Count(a => a.Module == module && a.IsOverdue);
            int completed = assignments.Count(a => a.Module == module && a.IsCompleted);
            MessageBox.Show(
                string.Format(
                    "Module: {0}\nAssignments: {1}\nCompleted: {2}\nOverdue: {3}",
                    module, total, completed, overdue),
                "Module Details", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void btnAssignment_add_Click(object sender, EventArgs e)
        {
            string title = txtAssignmentName_Add.Text.Trim();
            string errorMessage;
            if (!TryValidateAssignmentInput(cmbAssignmentModule.SelectedItem, title, out errorMessage))
            {
                MessageBox.Show(errorMessage, "Add Assignment", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            assignments.Add(new Assignment(
                cmbAssignmentModule.SelectedItem.ToString(),
                title,
                dtpDueDate_add.Value.Date));
            RefreshAssignmentList();
            SaveData();
            txtAssignmentName_Add.Clear();
        }

        private void btnCompletedAssignment_Click(object sender, EventArgs e)
        {
            Assignment selected = GetSelectedAssignment();
            if (selected == null)
            {
                MessageBox.Show("Select an assignment first.", "Complete Assignment",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            selected.MarkAsCompleted();
            RefreshAssignmentList();
            SaveData();
        }

        private void btnAssignments_Click(object sender, EventArgs e)
        {
            showingOverdueOnly = !showingOverdueOnly;
            btnAssignments.Text = showingOverdueOnly ? "Show All Assignments" : "View Overdue Assignements";
            RefreshAssignmentList();
        }

        private void btnEditAssignment_Click(object sender, EventArgs e)
        {
            Assignment selected = GetSelectedAssignment();
            if (selected == null)
            {
                MessageBox.Show("Select an assignment first.", "Edit Assignment",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            txtAssignment_edit.Text = selected.Title;
            dateTimePicker1.Value = selected.DueDate;
            cmbAssignmentModule.SelectedItem = selected.Module;
        }

        private void btnAssignment_edit_Click(object sender, EventArgs e)
        {
            Assignment selected = GetSelectedAssignment();
            string title = txtAssignment_edit.Text.Trim();
            string errorMessage;

            if (selected == null)
            {
                MessageBox.Show("Select an assignment to update.", "Update Assignment",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!TryValidateAssignmentInput(cmbAssignmentModule.SelectedItem, title, out errorMessage))
            {
                MessageBox.Show(errorMessage, "Update Assignment", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            selected.Title = title;
            selected.DueDate = dateTimePicker1.Value.Date;
            selected.Module = cmbAssignmentModule.SelectedItem.ToString();

            RefreshAssignmentList();
            SaveData();
            txtAssignment_edit.Clear();
        }

        private void btnViewAssignmentDetails_Click(object sender, EventArgs e)
        {
            Assignment selected = GetSelectedAssignment();
            if (selected == null)
            {
                MessageBox.Show("Select an assignment to view its details.", "View Details",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            MessageBox.Show(selected.GetDetailsText(), "Assignment Details",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void btnDeleteAssignment_Click(object sender, EventArgs e)
        {
            Assignment selected = GetSelectedAssignment();
            if (selected == null)
            {
                MessageBox.Show("Select an assignment to delete.", "Delete Assignment",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult result = MessageBox.Show("Delete " + selected.Title + "?",
                "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result == DialogResult.Yes)
            {
                assignments.Remove(selected);
                RefreshAssignmentList();
                SaveData();
            }
        }

        private void btnSortAssignments_Click(object sender, EventArgs e)
        {
            CycleSortOption();
            RefreshAssignmentList();
        }

        private void btnRefreshStudyPlan_Click(object sender, EventArgs e)
        {
            RefreshStudyPlan();
        }

        private void lstCurrentAssignments_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= displayOrder.Count)
            {
                e.DrawBackground();
                return;
            }

            Assignment assignment = displayOrder[e.Index];
            System.Drawing.Color statusColor = GetStatusColor(assignment);
            string statusWord = assignment.IsCompleted ? "Done" : (assignment.IsOverdue ? "Overdue" : "Pending");

            e.DrawBackground();

            // Status badge on the right, mirroring the coloured "pill" look from the design mockup.
            System.Drawing.Rectangle chipBounds = new System.Drawing.Rectangle(
                e.Bounds.Right - 92, e.Bounds.Top + 2, 82, e.Bounds.Height - 4);
            using (System.Drawing.SolidBrush chipBrush = new System.Drawing.SolidBrush(
                System.Drawing.Color.FromArgb(45, statusColor.R, statusColor.G, statusColor.B)))
            {
                e.Graphics.FillRectangle(chipBrush, chipBounds);
            }
            using (System.Drawing.SolidBrush chipTextBrush = new System.Drawing.SolidBrush(statusColor))
            {
                e.Graphics.DrawString(statusWord, e.Font, chipTextBrush, chipBounds,
                    new System.Drawing.StringFormat
                    {
                        Alignment = System.Drawing.StringAlignment.Center,
                        LineAlignment = System.Drawing.StringAlignment.Center
                    });
            }

            // Module / title / due date text on the left.
            string rowText = string.Format("{0} | {1} ({2:dd MMM yyyy})",
                assignment.Module, assignment.Title, assignment.DueDate);
            System.Drawing.Rectangle textBounds = new System.Drawing.Rectangle(
                e.Bounds.Left + 2, e.Bounds.Top, e.Bounds.Width - 98, e.Bounds.Height);
            using (System.Drawing.SolidBrush textBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Black))
            {
                e.Graphics.DrawString(rowText, e.Font, textBrush, textBounds,
                    new System.Drawing.StringFormat { LineAlignment = System.Drawing.StringAlignment.Center });
            }
            e.DrawFocusRectangle();
        }

        private void txtSearch_TextChanged(object sender, EventArgs e)
        {
            if (txtSearch.Text == SearchPlaceholder)
            {
                return;
            }
            RefreshAssignmentList();
        }

        private void txtSearch_Enter(object sender, EventArgs e)
        {
            if (txtSearch.Text == SearchPlaceholder)
            {
                txtSearch.Clear();
                txtSearch.ForeColor = System.Drawing.Color.Black;
            }
        }

        private void txtSearch_Leave(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtSearch.Text))
            {
                txtSearch.Text = SearchPlaceholder;
                txtSearch.ForeColor = System.Drawing.Color.Gray;
            }
        }

        private void btnSaveAndExit_Click(object sender, EventArgs e)
        {
            SaveData();
            Close();
        }

        private void frmStudyAndAssignement_FormClosing(object sender, FormClosingEventArgs e)
        {
            countdownTimer.Stop();
            try
            {
                SaveData();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Your changes could not be saved: " + ex.Message,
                    "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void label2_Click(object sender, EventArgs e)
        {
        }

        private void btnGuide_Click(object sender, EventArgs e)
        {
            MessageBox.Show(
                "Add / Edit Assignment:\tpick a module, type a title and due date, then Add Assignment.\n\n" +
                "Search by module or title, or use Sort to cycle between due date, module and title.\n\n" +
                "View Overdue Assignments toggles the list to overdue items only; the list itself\n\n" +
                "colours overdue items red and completed items green so they're easy to spot.\n\n" +
                "Select a row to mark it complete, view its full details, edit it, or delete it.\n\n" +
                "The Study Plan tab shows a live countdown to what's next and a priority queue\n\n" +
                "of everything outstanding, oldest due date first.\n\n" +
                "Save and exit (or simply closing the app) writes your data to disk as JSON.\n",
                "Assignment Tracker Guide", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
