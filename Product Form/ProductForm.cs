using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Product_Form
{
    public partial class ProductForm : Form
    {
        static readonly string DataFile = Path.Combine(Application.StartupPath, "products.csv");
        readonly HashSet<string> _codes = new HashSet<string>();
        readonly ErrorProvider err = new ErrorProvider();
        Control[] _inputs;
        bool _busy;

        public ProductForm()
        {
            InitializeComponent();

            AcceptButton = btnSave;
            CancelButton = btnCancel;

            // Combo box items
            cboCategory.DropDownStyle = ComboBoxStyle.DropDownList;
            cboCategory.Items.AddRange(new object[]
            {
                "Beverages", "Bakery", "Dairy", "Canned Goods", "Frozen Goods", "Snacks",
                "Electronics", "Household", "Personal Care", "Stationery", "Others"
            });

            txtBarcode.MaxLength = 13;
            txtDescription.Multiline = true;
            txtDescription.AcceptsReturn = true;
            txtDescription.ScrollBars = ScrollBars.Vertical;

            _inputs = new Control[] { txtBarcode, txtName, txtDescription, cboCategory, txtCost, txtPrice, txtMarkup };

            if (File.Exists(DataFile))
                foreach (var line in File.ReadLines(DataFile).Skip(1))
                    _codes.Add(line.Split(',')[0].Trim('"'));

            Numeric(txtBarcode, false);
            foreach (var t in new[] { txtCost, txtPrice, txtMarkup })
            {
                var box = t;
                Numeric(box, true);
                box.TextAlign = HorizontalAlignment.Right;
                box.TextChanged += (s, e) => Recalc(box);
                box.Leave += (s, e) => { decimal v; if (Num(box, out v)) Set(box, v); };
            }

            txtBarcode.TextChanged += (s, e) =>
            {
                string c = txtBarcode.Text;
                if (c.Length == 0) { lblBarcodeStatus.Text = ""; return; }
                if (!Regex.IsMatch(c, @"^\d{8,13}$")) { lblBarcodeStatus.Text = "Barcode must be 8 to 13 digits."; lblBarcodeStatus.ForeColor = Color.Gray; }
                else if (_codes.Contains(c)) { lblBarcodeStatus.Text = "Barcode is already in use."; lblBarcodeStatus.ForeColor = Color.Firebrick; }
                else { lblBarcodeStatus.Text = "Barcode is available."; lblBarcodeStatus.ForeColor = Color.SeaGreen; }
            };

            foreach (var c in _inputs)
                c.TextChanged += (s, e) => err.SetError((Control)s, "");

            btnSave.Click += (s, e) => Save();
            btnCancel.Click += (s, e) => Close();
            btnClear.Click += (s, e) =>
            {
                if (!Dirty || Ask("Clear all fields?", "Clear")) Reset();
            };
            FormClosing += (s, e) =>
            {
                if (e.CloseReason == CloseReason.UserClosing && Dirty && !Ask("Unsaved changes will be lost. Close anyway?", "Cancel"))
                    e.Cancel = true;
            };

            Reset();
        }

        bool Dirty { get { return _inputs.Any(c => c.Text.Length > 0); } }

        static bool Ask(string msg, string title)
        {
            return MessageBox.Show(msg, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        static bool Num(TextBox t, out decimal v)
        {
            return decimal.TryParse(t.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out v);
        }

        void Set(TextBox t, decimal v)
        {
            _busy = true;
            t.Text = Math.Round(v, 2).ToString("0.00", CultureInfo.CurrentCulture);
            _busy = false;
        }

        static void Numeric(TextBox t, bool allowDecimal)
        {
            t.KeyPress += (s, e) =>
            {
                char sep = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator[0];
                e.Handled = !(char.IsControl(e.KeyChar) || char.IsDigit(e.KeyChar) ||
                              (allowDecimal && e.KeyChar == sep && !t.Text.Contains(sep.ToString())));
            };
        }

        void Recalc(TextBox src)
        {
            decimal c, p, m;
            if (_busy || !Num(txtCost, out c) || c <= 0) return;
            if (src == txtMarkup) { if (Num(txtMarkup, out m)) Set(txtPrice, c * (1 + m / 100)); }
            else if (Num(txtPrice, out p)) Set(txtMarkup, (p - c) / c * 100);
            else if (src == txtCost && Num(txtMarkup, out m)) Set(txtPrice, c * (1 + m / 100));
        }

        void Reset()
        {
            err.Clear();
            _busy = true;
            foreach (var c in _inputs) c.Text = "";
            _busy = false;
            cboCategory.SelectedIndex = -1;
            rdoLocal.Checked = true;
            chkActive.Checked = true;
            txtBarcode.Focus();
        }

        bool IsValid()
        {
            err.Clear();
            Control first = null;
            Action<Control, string> fail = (c, m) => { err.SetError(c, m); if (first == null) first = c; };
            decimal cost, price, mk;

            string code = txtBarcode.Text;
            if (!Regex.IsMatch(code, @"^\d{8,13}$")) fail(txtBarcode, "Barcode must be 8 to 13 digits.");
            else if (_codes.Contains(code)) fail(txtBarcode, "This barcode is already in use.");
            if (txtName.Text.Trim().Length < 2) fail(txtName, "Name must be at least 2 characters.");
            if (cboCategory.SelectedIndex < 0) fail(cboCategory, "Please select a category.");
            if (!Num(txtCost, out cost) || cost <= 0) fail(txtCost, "Enter a cost greater than 0.");
            if (!Num(txtPrice, out price) || price <= 0) fail(txtPrice, "Enter a price greater than 0.");
            if (!Num(txtMarkup, out mk) || mk < -100) fail(txtMarkup, "Enter a valid markup (-100% or more).");

            if (first == null) return true;
            first.Focus();
            MessageBox.Show("Please correct the fields marked with the error icon.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        void Save()
        {
            if (!IsValid()) return;
            decimal cost, price, markup;
            Num(txtCost, out cost); Num(txtPrice, out price); Num(txtMarkup, out markup);

            if (price < cost && !Ask("The price is below the cost (sold at a loss). Save anyway?", "Price below cost"))
            {
                txtPrice.Focus();
                return;
            }

            var inv = CultureInfo.InvariantCulture;
            Func<string, string> q = s => "\"" + s.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
            string row = string.Join(",", q(txtBarcode.Text), q(txtName.Text.Trim()), q(txtDescription.Text.Trim()), q(cboCategory.Text),
                rdoLocal.Checked ? "Local" : "Abroad", chkActive.Checked ? "true" : "false",
                cost.ToString("0.00", inv), price.ToString("0.00", inv), markup.ToString("0.00", inv));

            try
            {
                if (!File.Exists(DataFile))
                    File.WriteAllText(DataFile, "Barcode,Name,Description,Category,Origin,Active,Cost,Price,MarkupPercent\r\n");
                File.AppendAllText(DataFile, row + "\r\n");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _codes.Add(txtBarcode.Text);
            MessageBox.Show(string.Format("Saved: {0}\nCost {1:N2}  Price {2:N2}  Markup {3:N2}%", txtName.Text.Trim(), cost, price, markup),
                "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Reset();
        }
    }
}