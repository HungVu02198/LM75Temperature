using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Threading;

using System.IO.Ports;
using ZedGraph;
using Microsoft.Office.Interop.Excel;

using FireSharp.Config;
using FireSharp.Interfaces;
using FireSharp.Response;

namespace LM75_Desktop
{
    public partial class Form1 : Form
    {
        #region Properties
        // Cấu hình FireBase
        IFirebaseConfig config = new FirebaseConfig
        {
            AuthSecret = "U4IhdoFF0gt2blxiSRPgvUUjmoswxyMhEtX5JaCx",
            BasePath = "https://lm75-a9a1d.firebaseio.com/"
        };
        IFirebaseClient client;

        SerialPort serialPort = null;
        // Thông tin Byte địa chỉ điểm đo
        private const int POINT_TEPORATURE_1_BIT_INDEX = 144;
        private const int POINT_TEPORATURE_2_BIT_INDEX = 146;
        private int state = -1;
        private int index = -1;
        // Danh sách cổng COM
        List<String> lstPortCOM = new List<string> () { "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM10" };

        // Danh sách BaundRate 
        List<int> lstBaudRate = new List<int> () { 110, 300, 1200, 2400, 4800, 9600, 19200, 38400, 57600 };

        // Danh sách thông tin nhiệt độ
        List<TemperatureInfo> lstTemperatureInfo = new List<TemperatureInfo>() { };

        // Thông tin lần đo
        TemperatureInfo newTemperatureInfo = new TemperatureInfo();
        TemperatureInfo cacheTemperatureInfo = new TemperatureInfo();

        // Chỉ số điểm đo
        int numberPoint;

        // Byte dữ liệu từ IC
        // 3 bit cao
        int number1;

        // 3 bit thấp
        int number2;

        // Biểu đồ hiển thị biến đổi nhiệt độ
        GraphPane myPane;
        GraphPane myPane1;

        // Biểu đồ đường điểm đo 1
        RollingPointPairList listPoint1 = new RollingPointPairList(10000);

        // Biểu đồ đường điểm đo 2
        RollingPointPairList listPoint2 = new RollingPointPairList(10000);

        // Thời điểm đo
        double timeSeconds = 0;

        int count = 0;
        Boolean isStopView = false;
        double randomTem = 0;
        #endregion

        #region Methods
        #region Setup Form hiển thị
        public Form1()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Form Load 
        /// </summary>
        private void Form1_Load(object sender, EventArgs e)
        {
            // SetUp cổng COM 
            serialPort = new SerialPort();
            serialPort.DataReceived += new SerialDataReceivedEventHandler(DataReceivedHandler);
            serialPort.PortName = "COM10";
            serialPort.BaudRate = 9600;
            serialPort.DataBits = 8;
            serialPort.StopBits = StopBits.One;
            serialPort.Parity = Parity.None;
            client = new FireSharp.FirebaseClient(config);
            // Thông tin biểu đồ
            myPane = zedGraphControl.GraphPane;
            myPane.Title.Text = "Điểm đo 1";
            myPane.XAxis.Title.Text = "Thời gian(s)";
            myPane.YAxis.Title.Text = "Nhiệt độ điểm đo";

            myPane1 = zedGraphControl1.GraphPane;
            myPane1.Title.Text = "Điểm đo 2";
            myPane1.XAxis.Title.Text = "Thời gian(s)";
            myPane1.YAxis.Title.Text = "Nhiệt độ điểm đo";
        }
        #endregion

        #region Đọc dữ liệu từ ATMEGA
        /// <summary>
        /// Đọc dữ liệu từ ATMEGA
        /// </summary>
        private void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
        {

            SerialPort sp = (SerialPort)sender;
            int indata = sp.ReadByte();
            if (indata == POINT_TEPORATURE_1_BIT_INDEX || indata == POINT_TEPORATURE_2_BIT_INDEX)
            {
                state = 0;
                index = indata;
                return;
            }

            if (state == 0)
            {
                state = 1;
                number1 = indata;
                return;
            }

            if (state == 1)
            {
                number2 = indata;
                state = -1;
                if (index == POINT_TEPORATURE_1_BIT_INDEX)
                {
                    newTemperatureInfo.temperaturePoint1 = getPointTemperature(1);
                }
                else
                {
                    newTemperatureInfo.temperaturePoint2 = getPointTemperature(2);
                    addTemperature();
                    this.count++;
                }
                return;
            }
        }

        /// <summary>
        /// Lấy thông tin nhiệt độ điểm đo
        /// </summary>
        /// <param name="pointNumber">Số hiệu điểm đo</param>
        private double getPointTemperature(int pointNumber)
        {
            double tem = number2 + (number1 / 256.00);
            numberPoint = pointNumber;
            number1 = 0;
            number2 = 0;
            return tem;
        }

        /// <summary>
        /// Thêm thông tin lần đo -> Vẽ biểu đồ
        /// </summary>
        private void addTemperature()
        {
            if (!isStopView) {
                timeSeconds = (double)(timeSeconds + 0.2);
                getRandom();
                setRealTimeChart();
            }
            UpdateTextBox("");
            newTemperatureInfo.timeReceived = DateTime.Now.TimeOfDay.ToString().Substring(0,10);
            lstTemperatureInfo.Add(newTemperatureInfo);
            sentFireBaseData();
            cacheTemperatureInfo = newTemperatureInfo;
            newTemperatureInfo = new TemperatureInfo();
        }

        public void UpdateTextBox(string value)
        {
            if (InvokeRequired)
            {
                this.Invoke(new Action<string>(UpdateTextBox), new object[] { value });
                return;
            }
            textBox5.Text = (newTemperatureInfo.temperaturePoint1 + randomTem).ToString().Substring(0, 5);
            textBox6.Text = (newTemperatureInfo.temperaturePoint2 + randomTem).ToString().Substring(0, 5);
        }

        /// <summary>
        /// Vẽ biểu đồ theo thời gian thực
        /// </summary>
        private void setRealTimeChart()
        {
            setChart(zedGraphControl, myPane, listPoint1, newTemperatureInfo.temperaturePoint1);
            setChart(zedGraphControl1, myPane1, listPoint2, newTemperatureInfo.temperaturePoint2);
        }

        private void setChart(ZedGraphControl zedGraphControl, GraphPane graphPane, RollingPointPairList listPoint, double temperature)
        {
            zedGraphControl.GraphPane.CurveList.Clear();
            zedGraphControl.AxisChange();
            zedGraphControl.Invalidate();
            listPoint.Add(timeSeconds, temperature + this.randomTem);
            graphPane.AddCurve("Điểm đo ", listPoint, Color.Red, SymbolType.Diamond);

            zedGraphControl.AxisChange();
            zedGraphControl.Invalidate();
        }
        #endregion

        #region Đẩy dữ liệu từ ATMEGA
        /// <summary>
        /// Nhận dữ liệu ToS cảm ứng nhiệt (1Byte)
        /// </summary>
        /// TODO: Gửi thông tin ToS
        private void textBox1_TextChanged(object sender, EventArgs e)
        {

        }

        private void getRandom()
        {
            Random random = new Random();
            if (random.Next(0, 4) % 2 == 0) this.randomTem = random.NextDouble();
            else this.randomTem = random.NextDouble();
        }

        /// <summary>
        /// Bật chuông cảnh báo
        /// </summary>
        /// <param name="sender">Du lieu khi Click = "1"</param>
        private void Button1_Click(object sender, EventArgs e)
        {
            //serialPort.Write(new char[] { (char)129 }, 0, 1);
            int Tos = int.Parse(textBox1.Text);
            if (Tos > 125) Tos = 125;
            if (Tos < -55) Tos = -55;
            serialPort.Write(new char[] { (char)Tos }, 0, 1);
        }

        /// <summary>
        /// Tắt chuông cảnh báo
        /// </summary>
        /// <param name="sender">Du lieu khi Click = "0"</param>
        private void Button2_Click(object sender, EventArgs e)
        {
            serialPort.Write(new char[] { '0' }, 0, 1);
        }
        #endregion

        #region Lưu gữ liệu FireBase
        private async void sentFireBaseData()
        {
            FirebaseResponse response = await client.UpdateTaskAsync("TemperatureInfo/", newTemperatureInfo);
        }
        #endregion

        #region Event Button
        /// <summary>
        /// Ngắt kết nối Port
        /// </summary>
        private void button4_Click(object sender, EventArgs e)
        {
            resetValue();
        }

        /// <summary>
        /// Mở kết nối nhận dữ liệu
        /// </summary>
        private void button7_Click(object sender, EventArgs e)
        {
            try
            {
                serialPort.Open();
                button7.Visible = false;
                comboBox1.Visible = false;
                comboBox2.Visible = false;
                button8.Visible = true;
                button9.Visible = true;
                button4.Visible = true;
                textBox2.Visible = true;
                textBox3.Visible = true;
                textBox2.Text = lstPortCOM[comboBox1.SelectedIndex];
                textBox3.Text = lstBaudRate[comboBox2.SelectedIndex].ToString();
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message);
            }
        }

        /// <summary>
        /// Clear Form dữ liệu
        /// </summary>
        private void button5_Click(object sender, EventArgs e)
        {
            listView1_SelectedIndexChanged(new object(), new EventArgs());
            this.lstTemperatureInfo = new List<TemperatureInfo>() { };
        }

        /// <summary>
        /// Hiển thị danh sách kết quả đo
        /// </summary>
        private void listView1_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                TemperatureInfo temperature = lstTemperatureInfo.LastOrDefault<TemperatureInfo>();
                addListViewItem(temperature);
                //listView1.Items[listView1.Items.Count - 1].EnsureVisible();
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception.Message);
                addListViewItem(cacheTemperatureInfo);
            }
        }

        /// <summary>
        /// Lấy thông tin tại thời điểm đo
        /// </summary>
        /// <param name="temperature">Thông tin điểm đo</param>
        private void addListViewItem(TemperatureInfo temperature)
        {
            ListViewItem item1 = new ListViewItem();
            item1.SubItems.Add(temperature.timeReceived);
            item1.SubItems.Add((temperature.temperaturePoint1 + randomTem).ToString().Substring(0, 5));
            item1.SubItems.Add((temperature.temperaturePoint2 + randomTem).ToString().Substring(0, 5));
            listView1.Items.Add(item1);
        }


        /// <summary>
        /// Chọn Cổng COM ảo kết nối
        /// </summary>
        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            comboBox1.DataSource = lstPortCOM;
            var portNumber = comboBox1.SelectedIndex;
            serialPort.PortName = lstPortCOM[portNumber];
        }

        /// <summary>
        /// Chọn BaundRate kết nối
        /// </summary>
        private void comboBox2_SelectedIndexChanged_1(object sender, EventArgs e)
        {
            comboBox2.DataSource = lstBaudRate;
            var baundRate = comboBox2.SelectedIndex;
            serialPort.BaudRate = lstBaudRate[baundRate];
        }

        /// <summary>
        /// Reset biểu đồ 
        /// </summary>
        private void button8_Click(object sender, EventArgs e)
        {
            listPoint1.Clear();
            listPoint2.Clear();
            this.timeSeconds = 0;
            this.isStopView = false;
            button9.Visible = true;
        }

        /// <summary>
        /// Stop thêm dữ liệu biểu đồ
        /// </summary>
        private void button9_Click(object sender, EventArgs e)
        {
            this.isStopView = true;
            button9.Visible = false;
        }

        /// <summary>
        /// Reset trạng thái
        /// </summary>
        private void button3_Click(object sender, EventArgs e)
        {
            resetValue();
            serialPort.BaudRate = 9600;
            serialPort.PortName = "COM10";
            comboBox1.SelectedIndex = 9;
            comboBox2.SelectedIndex = 5;
        }

        /// <summary>
        /// Reset trạng thái khi Click Reset, Ngắt kết nối
        /// </summary>
        private void resetValue()
        {
            serialPort.Close();
            // Reset giá trị
            this.cacheTemperatureInfo = new TemperatureInfo();
            this.newTemperatureInfo = new TemperatureInfo();
            this.lstTemperatureInfo = new List<TemperatureInfo>() { };
            this.number1 = 0;
            this.number2 = 0;
            this.timeSeconds = 0;
            listPoint1.Clear();
            listPoint2.Clear();
            this.isStopView = false;
            listView1.Items.Clear();

            // Button hiển thị
            button4.Visible = false;
            button7.Visible = true;
            comboBox1.Visible = true;
            comboBox2.Visible = true;
            textBox1.ReadOnly = false;
            textBox2.Visible = false;
            textBox3.Visible = false;
            button8.Visible = false;
            button9.Visible = false;
        }
        #endregion
        #endregion
    }

    #region Thông tin lần đo
    public class TemperatureInfo {
        public double temperaturePoint1 { get; set; }

        public double temperaturePoint2 { get; set; }

        public string timeReceived { get; set; }

        public TemperatureInfo()
        {

        }
    }
    #endregion
}