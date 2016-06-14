﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using GoodAI.Arnold.Project;
using GoodAI.Arnold.Properties;
using ScintillaNET;
using WeifenLuo.WinFormsUI.Docking;

namespace GoodAI.Arnold.Forms
{
    public partial class JsonEditForm : DockContent
    {
        private static readonly string m_defaultBlueprint = Resources.DefaultBlueprint;

        private readonly IDesigner m_designer;

        public JsonEditForm(IDesigner designer)
        {
            m_designer = designer;

            InitializeComponent();

            content.TextChanged += OnTextChanged;
            content.Text = m_defaultBlueprint;

            content.Lexer = Lexer.Cpp;

            content.StyleResetDefault();
            content.Styles[Style.Default].Font = "Consolas";
            content.Styles[Style.Default].Size = 10;
            content.StyleClearAll();

            content.Styles[Style.Cpp.Default].ForeColor = Color.Silver;
            content.Styles[Style.Cpp.Comment].ForeColor = Color.Green;
            content.Styles[Style.Cpp.CommentLine].ForeColor = Color.Green;
            content.Styles[Style.Cpp.Number].ForeColor = Color.Teal;
            content.Styles[Style.Cpp.Word].ForeColor = Color.Blue;
            content.Styles[Style.Cpp.String].ForeColor = Color.Blue;
            content.Styles[Style.Cpp.Character].ForeColor = Color.Maroon;
            content.Styles[Style.Cpp.Preprocessor].ForeColor = Color.Maroon;
            content.Styles[Style.Cpp.Operator].ForeColor = Color.Firebrick;
            content.Styles[Style.Cpp.StringEol].BackColor = Color.Pink;
            content.Styles[Style.Cpp.Verbatim].ForeColor = Color.FromArgb(-6089451);
            content.Styles[Style.Cpp.CommentLineDoc].ForeColor = Color.Gray;
            content.Styles[Style.Cpp.Word2].ForeColor = Color.Blue;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            content.TextChanged -= OnTextChanged;
        }

        private void OnTextChanged(object sender, EventArgs e)
        {
            m_designer.Blueprint = content.Text;
        }
    }
}
