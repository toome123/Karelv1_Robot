﻿using Diagrams;
using DiO_CS_KarelV1_TEST.Properties;
using KarelRobot;
using KarelRobot.Events;
using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Utils;

using DatabaseConnection;
using DatabaseConnection.Device.Sensors;
using DatabaseConnection.Device.Actuators;
using InputMethods.KeystrokEventGenerator;
using DatabaseConnection.Units;

// 
// Robot tasks ...
// TODO: Add Tweeter publisher.
// TODO: Create WEB application that listen for the twiits and show it on the screen.
// TODO: Create gliph recognition module.
// TODO: Connect to NoSQL.
// TODO: Add message when robot is moving and when it is not.
// TODO: Create roadmap of the robot.
// TODO: Add sensors.
// TODO: Add indication LEDs.
//

namespace DiO_CS_KarelV1_TEST
{
    /// <summary>
    /// Main form of robot controller.
    /// </summary>
    public partial class MainForm : Form
    {

        #region Variables

        /// <summary>
        /// Robot seral port name.
        /// </summary>
        private string robotSerialPortName = "";

        /// <summary>
        /// Robot communication.
        /// </summary>
        private KarelV1 myRobot;

        /// <summary>
        /// Data generator.
        /// </summary>
        private DigramDataGenerator dataGenerator = new DigramDataGenerator();

        /// <summary>
        /// Circular diagram.
        /// </summary>
        private CircularDiagram myDiagram;

        /// <summary>
        /// Sensor data.
        /// </summary>
        private double[] sensorData = new double[181];

        /// <summary>
        /// Maximum distance index.
        /// </summary>
        private int maxDistanceIndex = 0;

        /// <summary>
        /// Maximum distance value.
        /// </summary>
        private double maxDistanceValue = -100;

        /// <summary>
        /// Update camera timer.
        /// </summary>
        private System.Windows.Forms.Timer cameraUpdateTimer;

        /// <summary>
        /// IP Camera for the glyph recogniser.
        /// </summary>
        private ImageSource.IpCamera ipCamera;

        private Bitmap capturedImage;

        private object syncLockCapture;

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor
        /// </summary>
        public MainForm()
        {
            InitializeComponent();

            this.cameraUpdateTimer = new System.Windows.Forms.Timer();
            this.cameraUpdateTimer.Stop();
            this.cameraUpdateTimer.Tick += this.cameraUpdateTimer_Tick;

            this.syncLockCapture = new object();

            #region Keyboard

            // Attach keyboard strokes events.
            KeystrokMessageFilter ksMessageFilter = new KeystrokMessageFilter();
            ksMessageFilter.OnOpenConfigurationManager += ksMessageFilter_OnOpenConfigurationManager;
            ksMessageFilter.OnExitApplication += ksMessageFilter_OnExitApplication;
            ksMessageFilter.OnForward += ksMessageFilter_OnForward;
            ksMessageFilter.OnBackward += ksMessageFilter_OnBackward;
            ksMessageFilter.OnLeft += ksMessageFilter_OnLeft;
            ksMessageFilter.OnRight += ksMessageFilter_OnRight;
            Application.AddMessageFilter(ksMessageFilter);
            #endregion

        }

        #endregion

        #region Private

        /// <summary>
        /// Search serial port.
        /// </summary>
        private void SearchForPorts()
        {
            this.portsToolStripMenuItem.DropDown.Items.Clear();

            string[] portNames = System.IO.Ports.SerialPort.GetPortNames();

            if (portNames.Length == 0)
            {
                return;
            }

            foreach (string item in portNames)
            {
                //store the each retrieved available prot names into the MenuItems...
                this.portsToolStripMenuItem.DropDown.Items.Add(item);
            }

            foreach (ToolStripMenuItem item in this.portsToolStripMenuItem.DropDown.Items)
            {
                item.Click += mItPorts_Click;
                item.Enabled = true;
                item.Checked = false;
            }
        }

        /// <summary>
        /// Set robot position.
        /// </summary>
        /// <param name="text">Position</param>
        private void SetRobotPos(double distance, double alpha)
        {
            string text = String.Format("Position\r\nDistance: {0:3F}[st]\r\nAlpha: {0:3F}[st]", distance, alpha);

            // + Environment.NewLine
            if (this.lblRobotPosition.InvokeRequired)
            {
                this.lblRobotPosition.BeginInvoke((MethodInvoker)delegate()
                {
                    this.lblRobotPosition.Text = text;
                });
            }
            else
            {
                this.lblRobotPosition.Text = text;
            }
        }

        /// <summary>
        /// Set left sensor progres bar.
        /// </summary>
        /// <param name="value"></param>
        private void SetLeftSensor(int value)
        {
            if (this.prbLeftSensor.InvokeRequired)
            {
                this.prbLeftSensor.BeginInvoke((MethodInvoker)delegate()
                {
                    this.prbLeftSensor.Value = value;
                });
            }
            else
            {
                this.prbLeftSensor.Value = value;
            }
        }

        /// <summary>
        /// Set righr sensor progres bar.
        /// </summary>
        /// <param name="value"></param>
        private void SetRightSensor(int value)
        {
            if (this.prbRightSensor.InvokeRequired)
            {
                this.prbRightSensor.BeginInvoke((MethodInvoker)delegate()
                {
                    this.prbRightSensor.Value = value;
                });
            }
            else
            {
                this.prbRightSensor.Value = value;
            }
        }

        /// <summary>
        /// Add a status to the status list..
        /// </summary>
        /// <param name="text"></param>
        /// <param name="textColor"></param>
        private void AddStatus(string text, Color textColor)
        {
            if (this.txtState.InvokeRequired)
            {
                this.txtState.BeginInvoke((MethodInvoker)delegate()
                {
                    this.txtState.AppendText(text);
                    this.txtState.BackColor = textColor;
                });
            }
            else
            {
                this.txtState.AppendText(text);
                this.txtState.BackColor = textColor;
            }
        }

        /// <summary>
        /// Return curret data and time.
        /// </summary>
        /// <returns></returns>
        private string GetDateTime()
        {
            return DateTime.Now.ToString("yyyy.MM.dd/HH:mm:ss.fff", System.Globalization.DateTimeFormatInfo.InvariantInfo);
        }

        /// <summary>
        /// Capture the navigation video camera.
        /// </summary>
        private void CaptureCamera()
        {
            lock (this.syncLockCapture)
            {
                // IP Camera:    http://http://192.168.1.1/cgi-bin/image.jpg
                // Mobile phone: http://192.168.0.104:8080/photoaf.jpg

                Uri uri;
                bool isValidUri = Uri.TryCreate(this.tbCameraIP.Text, UriKind.Absolute, out uri);
                if (!isValidUri)
                {
                    return;
                }

                if (this.ipCamera == null)
                {
                    this.ipCamera = new ImageSource.IpCamera(uri);
                    //this.ipCamera.Torch = true;
                }

                if (uri != this.ipCamera.URI)
                {
                    this.ipCamera = new ImageSource.IpCamera(uri);
                    //this.ipCamera.Torch = true;
                }

                if (this.capturedImage != null) this.capturedImage.Dispose();

                try
                {
                    this.capturedImage = this.ipCamera.Capture();
                    this.pbGlyph.Image = AppUtils.FitImage(this.capturedImage, this.pbGlyph.Size);
                }
                catch (Exception exception)
                {
                    this.AddStatus(exception.ToString() + Environment.NewLine, Color.White);
                    return;
                }
            }
        }

        /// <summary>
        /// Date time.
        /// </summary>
        /// <returns></returns>
        private string DateTimeNow()
        {
            // Get date and time.
            return DateTime.Now.ToString("yyyy.MM.dd/HH:mm:ss.fff", System.Globalization.DateTimeFormatInfo.InvariantInfo);
        }

        #endregion

        #region Robot

        private void ConnectToRobot()
        {
            try
            {
                // COM55 - Bluetooth
                // COM67 - Cabel
                // COM73 - cabel
                this.myRobot = new KarelV1(this.robotSerialPortName);
                //this.myRobot.Message += myRobot_Message;
                this.myRobot.Sensors += myRobot_Sensors;
                this.myRobot.UltraSonicSensor += myRobot_UltraSonicSensor;
                this.myRobot.Stoped += myRobot_Stoped;
                this.myRobot.GreatingsMessage += myRobot_GreatingsMessage;
                this.myRobot.MotionState += myRobot_MotionState;
                this.myRobot.Connect();
                this.myRobot.Reset();
            }
            catch (Exception exception)
            {
                this.AddStatus(exception.ToString(), Color.White);
            }
        }

        private void DisconnectFromRobot()
        {
            try
            {
                if (this.myRobot != null && this.myRobot.IsConnected)
                {
                    this.myRobot.Disconnect();
                }
            }
            catch (Exception exception)
            {
                this.AddStatus(exception.ToString(), Color.White);
            }
        }

        private void MoveForward()
        {
            if (this.myRobot != null)
            {
                // 10 mm
                //int steps = (int)(100.0f / Settings.Default.StepToDegreeBy8);
                this.myRobot.Move(100);
            }
        }

        private void MoveBackward()
        {
            if (this.myRobot != null)
            {
                // -10 mm
                this.myRobot.Move(-100);
            }
        }

        private void MoveLeft()
        {
            if (this.myRobot != null)
            {
                // -10 deg
                this.myRobot.Rotate(-100);
            }
        }

        private void MoveRight()
        {
            if (this.myRobot != null)
            {
                // 10 deg
                this.myRobot.Rotate(100);
            }
        }

        private void myRobot_Message(object sender, StringEventArgs e)
        {
            string infoLine = String.Format("{0} -> {1}", this.GetDateTime(), e.Message);
            this.AddStatus(infoLine + Environment.NewLine, Color.White);
        }

        private void myRobot_GreatingsMessage(object sender, StringEventArgs e)
        {
            string infoLine = String.Format("{0} -> {1}", this.GetDateTime(), e.Message);
            this.AddStatus(infoLine + Environment.NewLine, Color.White);
        }

        private void myRobot_Stoped(object sender, EventArgs e)
        {
            string infoLine = String.Format("{0} -> Stoped", this.GetDateTime());
            this.AddStatus(infoLine + Environment.NewLine, Color.White);
        }

        private void myRobot_UltraSonicSensor(object sender, UltraSonicSensorEventArgs e)
        {
            float scale = 2.0f;
            string infoLine = String.Format("{0} -> US Sensor: {1}[deg] {2}[mm]", this.GetDateTime(), e.Position, e.Distance);
            this.AddStatus(infoLine + Environment.NewLine, Color.White);
            this.UpdateDiagram((int)e.Position, e.Distance * scale);
        }

        private void myRobot_Sensors(object sender, SensorsEventArgs e)
        {
            string infoLine = String.Format("{0} -> Sensors: {1} {2}", this.GetDateTime(), e.Left, e.Right);
            this.AddStatus(infoLine + Environment.NewLine, Color.White);
            this.SetLeftSensor((int)Math.Floor(e.Left));
            this.SetRightSensor((int)Math.Floor(e.Right));
        }

        private void myRobot_MotionState(object sender, RobotPositionEventArgs e)
        {
            string infoLine = String.Format("{0} -> Position: A:{1} D:{2}", this.GetDateTime(), e.Alpha, e.Distance);
            this.AddStatus(infoLine + Environment.NewLine, Color.White);
            this.SetRobotPos(e.Distance, e.Alpha);
        }

        #endregion

        #region MainForm

        private void MainForm_Load(object sender, EventArgs e)
        {
            // Double buffer optimization.
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);

            // Font configuration.
            foreach (Control control in this.Controls)
            {
                if (control.GetType() == typeof(Button))
                {
                    control.Font = new Font(FontFamily.GenericSansSerif, 10.0F, FontStyle.Bold);
                }
            }

            //
            this.myDiagram = new CircularDiagram(this.pbSensorView.Size);
            this.myDiagram.DiagramName = "Ultra Sonic Sensor";

            this.SearchForPorts();
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            this.DisconnectFromRobot();
        }

        #endregion

        #region Buttons

        private void btnForward_Click(object sender, EventArgs e)
        {
            this.MoveForward();
        }

        private void btnBackward_Click(object sender, EventArgs e)
        {
            this.MoveBackward();
        }

        private void btnLeft_Click(object sender, EventArgs e)
        {
            this.MoveLeft();
        }

        private void btnRight_Click(object sender, EventArgs e)
        {
            this.MoveRight();
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            if (this.myRobot != null && this.myRobot.IsConnected)
            {
                this.myRobot.Stop();
            }
        }

        private void btnGetSensors_Click(object sender, EventArgs e)
        {
            if (this.myRobot != null && this.myRobot.IsConnected)
            {
                this.myRobot.GetSensors();
            }
        }

        private void btnGetUltrasonic_Click(object sender, EventArgs e)
        {
            if (this.myRobot != null && this.myRobot.IsConnected)
            {
                int position = 0;

                if (int.TryParse(this.tbSensorPosition.Text, out position))
                {
                    if (position > 180 || position < 0)
                    {
                        return;
                    }

                    this.myRobot.GetUltraSonic(position);
                }
                else
                {
                    for (int index = 0; index < this.sensorData.Length; index++)
                    {
                        maxDistanceValue = this.sensorData[index] = 0.0d;
                    }

                    this.maxDistanceValue = double.MinValue;
                    this.maxDistanceIndex = 0;

                    this.myRobot.GetUltraSonic();
                }
            }
        }

        private void btnReset_Click(object sender, EventArgs e)
        {
            if (this.myRobot != null && this.myRobot.IsConnected)
            {
                this.myRobot.Reset();
            }
        }

        private void btnGetRobotPos_Click(object sender, EventArgs e)
        {
            if (this.myRobot != null && this.myRobot.IsConnected)
            {
                // -10 mm
                this.myRobot.GetPosition();
            }
        }

        private void btnCapture_Click(object sender, EventArgs e)
        {
            Thread captureThread = new Thread(
                new ThreadStart(
                    delegate()
                    {
                        this.CaptureCamera();
                    }
            ));

            captureThread.Start();
        }

        #endregion

        #region pbSensorView

        private void UpdateDiagram(int position, float distance)
        {
            if (this.pbSensorView.InvokeRequired)
            {
                this.pbSensorView.BeginInvoke((MethodInvoker)delegate()
                {
                    this.sensorData[position] = distance;
                    //this.myDiagram.SetData(this.sensorData);
                    this.myDiagram.SetData(position, distance);

                    for (int index = 0; index < this.sensorData.Length; index++)
                    {
                        if (this.maxDistanceValue < this.sensorData[index])
                        {
                            this.maxDistanceValue = this.sensorData[index];
                            this.maxDistanceIndex = index;
                        }
                    }

                    this.pbSensorView.Refresh();

                });
            }
            else
            {
                this.sensorData[position] = distance;
                //this.myDiagram.SetData(this.sensorData);
                this.myDiagram.SetData(position, distance);

                for (int index = 0; index < this.sensorData.Length; index++)
                {
                    if (this.maxDistanceValue < this.sensorData[index])
                    {
                        this.maxDistanceValue = this.sensorData[index];
                        this.maxDistanceIndex = index;
                    }
                }
                this.pbSensorView.Refresh();
            }
        }

        private void pbSensorView_Paint(object sender, PaintEventArgs e)
        {
            this.myDiagram.Draw(e.Graphics);
            this.myDiagram.DrawLine(e.Graphics, this.maxDistanceIndex);
        }

        #endregion

        #region Menu Item

        private void mItPorts_Click(object sender, EventArgs e)
        {
            this.DisconnectFromRobot();
            ToolStripMenuItem item = (ToolStripMenuItem)sender;
            this.robotSerialPortName = item.Text;
            this.ConnectToRobot();

            if (this.myRobot.IsConnected)
            {
                this.lblIsConnected.Text = String.Format("Connected: {0}", robotSerialPortName);
                item.Checked = true;
            }
            else
            {
                this.lblIsConnected.Text = "Not Connected";
                item.Checked = false;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void connectionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.SearchForPorts();
        }
        
        #endregion

        #region Continues Capture Check

        private void chkContinuesCapture_CheckedChanged(object sender, EventArgs e)
        {
            int interval = 500;
            bool IsValidTime = int.TryParse(this.tbCameraUpdateTime.Text, out interval);
            if (!IsValidTime)
            {
                return;
            }

            this.cameraUpdateTimer.Interval = interval;

            if (this.chkContinuesCapture.Checked)
            {
                this.cameraUpdateTimer.Start();
            }
            else
            {
                this.cameraUpdateTimer.Stop();
            }

            this.btnCapture.Enabled = !this.chkContinuesCapture.Checked;
        }

        #endregion

        #region Camera Update Timer

        private void cameraUpdateTimer_Tick(object sender, EventArgs e)
        {
            this.CaptureCamera();
        }

        #endregion.Events

        #region Text Box

        private void tbSensorPosition_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Enter)
            {
                this.btnGetUltrasonic_Click(sender, new EventArgs());
            }
        }

        #endregion

        #region Keyboard

        private void ksMessageFilter_OnExitApplication(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void ksMessageFilter_OnOpenConfigurationManager(object sender, EventArgs e)
        {
            throw new NotImplementedException();
        }

        private void ksMessageFilter_OnRight(object sender, EventArgs e)
        {
            this.MoveRight();
        }

        private void ksMessageFilter_OnLeft(object sender, EventArgs e)
        {
            this.MoveLeft();
        }

        private void ksMessageFilter_OnBackward(object sender, EventArgs e)
        {
            this.MoveBackward();
        }

        private void ksMessageFilter_OnForward(object sender, EventArgs e)
        {
            this.MoveForward();
        }

        #endregion

        private void btnTastDatabase_Click(object sender, EventArgs e)
        {
            Thread comitThread = new Thread(
                new ThreadStart(
                    delegate()
                    {
                        try
                        {
                            // Database test.

                            #region Distance

                            // Create sensor.
                            Distance dstSens = new Distance();

                            // Date and time of mesurements.
                            dstSens.DateTime = DateTime.Now;

                            // Name of the sensor.
                            dstSens.Name = "Sensor 1";

                            // Description.
                            dstSens.Descripotion = "Ultrasonic sensor on the top of the robot.";

                            // Positional data.
                            dstSens.Position.Point.Unit = Scales.MM;
                            dstSens.Position.Point.X = 1.0f;
                            dstSens.Position.Point.Y = 2.0f;
                            dstSens.Position.Point.Z = 3.0f;
                            dstSens.Position.Orientation.Unit = Radial.Degree;
                            dstSens.Position.Orientation.A = 4.0f;
                            dstSens.Position.Orientation.B = 5.0f;
                            dstSens.Position.Orientation.C = 6.0f;

                            // Mesurements units.
                            dstSens.Unit = Scales.MM;
                            dstSens.Value = 100.0f;
                            dstSens.MaxValue = 50.0f;
                            dstSens.MinValue = 2000.0f;

                            #endregion

                            #region Humidity

                            // Create sensor.
                            Humidity humSens = new Humidity();

                            // Date and time of mesurements.
                            humSens.DateTime = DateTime.Now;

                            // Name of the sensor.
                            humSens.Name = "Sensor 2";

                            // Description.
                            humSens.Descripotion = "Humidite sensor on the back of the robot.";

                            // Positional data.
                            humSens.Position.Point.Unit = Scales.MM;
                            humSens.Position.Point.X = 1.0f;
                            humSens.Position.Point.Y = 2.0f;
                            humSens.Position.Point.Z = 3.0f;
                            humSens.Position.Orientation.Unit = Radial.Degree;
                            humSens.Position.Orientation.A = 4.0f;
                            humSens.Position.Orientation.B = 5.0f;
                            humSens.Position.Orientation.C = 6.0f;

                            // Mesurements units.
                            humSens.Unit = Scales.MM;
                            humSens.Value = 50.0f;
                            humSens.MaxValue = 100.0f;
                            humSens.MinValue = 0.0f;

                            #endregion

                            #region Presure

                            // Create sensor.
                            Pressure presSens = new Pressure();

                            // Date and time of mesurements.
                            presSens.DateTime = DateTime.Now;

                            // Name of the sensor.
                            presSens.Name = "Sensor 3";

                            // Description.
                            presSens.Descripotion = "Air tank sensor.";

                            // Positional data.
                            presSens.Position.Point.Unit = Scales.MM;
                            presSens.Position.Point.X = 1.0f;
                            presSens.Position.Point.Y = 2.0f;
                            presSens.Position.Point.Z = 3.0f;
                            presSens.Position.Orientation.Unit = Radial.Degree;
                            presSens.Position.Orientation.A = 4.0f;
                            presSens.Position.Orientation.B = 5.0f;
                            presSens.Position.Orientation.C = 6.0f;

                            // Mesurements units.
                            presSens.Unit = Pressures.Bar;
                            presSens.Value = 5.0f;
                            presSens.MaxValue = 10.0f;
                            presSens.MinValue = -1.0f;

                            #endregion

                            #region Temperature

                            // Create sensor.
                            Temperature tempSens = new Temperature();

                            // Date and time of mesurements.
                            tempSens.DateTime = DateTime.Now;

                            // Name of the sensor.
                            tempSens.Name = "Sensor 4";

                            // Description.
                            tempSens.Descripotion = "Left motor temperature sensor.";

                            // Positional data.
                            tempSens.Position.Point.Unit = Scales.MM;
                            tempSens.Position.Point.X = 1.0f;
                            tempSens.Position.Point.Y = 2.0f;
                            tempSens.Position.Point.Z = 3.0f;
                            tempSens.Position.Orientation.Unit = Radial.Degree;
                            tempSens.Position.Orientation.A = 4.0f;
                            tempSens.Position.Orientation.B = 5.0f;
                            tempSens.Position.Orientation.C = 6.0f;

                            // Mesurements units.
                            tempSens.Unit = TemperatureScale.Celsius;
                            tempSens.Value = 20.0f;
                            tempSens.MaxValue = 85.0f;
                            tempSens.MinValue = -20.0f;

                            #endregion

                            #region Left Stepper motor

                            // Create sensor.
                            StepperMotor lStepAct = new StepperMotor();

                            // Date and time of mesurements.
                            lStepAct.DateTime = DateTime.Now;

                            // Name of the sensor.
                            lStepAct.Name = "Left motor";

                            // Description.
                            lStepAct.Descripotion = "Left of the robot motor.";

                            // Positional data.
                            lStepAct.Position.Point.Unit = Scales.MM;
                            lStepAct.Position.Point.X = 1.0f;
                            lStepAct.Position.Point.Y = 2.0f;
                            lStepAct.Position.Point.Z = 3.0f;
                            lStepAct.Position.Orientation.Unit = Radial.Degree;
                            lStepAct.Position.Orientation.A = 4.0f;
                            lStepAct.Position.Orientation.B = 5.0f;
                            lStepAct.Position.Orientation.C = 6.0f;

                            // Mesurements units.
                            lStepAct.Unit = Scales.Steps;
                            lStepAct.Value = 20.0f;
                            lStepAct.MaxValue = 200.0f;
                            lStepAct.MinValue = 0.0f;
                            lStepAct.Acceleration = 50;
                            lStepAct.Jerk = 70;

                            #endregion

                            #region Right Stepper motor

                            // Create sensor.
                            StepperMotor rStepAct = new StepperMotor();

                            // Date and time of mesurements.
                            rStepAct.DateTime = DateTime.Now;

                            // Name of the sensor.
                            rStepAct.Name = "Left motor";

                            // Description.
                            rStepAct.Descripotion = "Left of the robot motor.";

                            // Positional data.
                            rStepAct.Position.Point.Unit = Scales.MM;
                            rStepAct.Position.Point.X = 1.0f;
                            rStepAct.Position.Point.Y = 2.0f;
                            rStepAct.Position.Point.Z = 3.0f;
                            rStepAct.Position.Orientation.Unit = Radial.Degree;
                            rStepAct.Position.Orientation.A = 4.0f;
                            rStepAct.Position.Orientation.B = 5.0f;
                            rStepAct.Position.Orientation.C = 6.0f;

                            // Mesurements units.
                            rStepAct.Unit = Scales.Steps;
                            rStepAct.Value = 20.0f;
                            rStepAct.MaxValue = 200.0f;
                            rStepAct.MinValue = 0.0f;
                            rStepAct.Acceleration = 50;
                            rStepAct.Jerk = 70;

                            #endregion

                            // Create DB.
                            DatabaseConnector db = new DatabaseConnector(new Uri(Settings.Default.DatabaseUri));

                            // Send data to the DB.
                            db.CommitDevice(dstSens);
                            db.CommitDevice(humSens);
                            db.CommitDevice(presSens);
                            db.CommitDevice(tempSens);
                            db.CommitDevice(lStepAct);
                            db.CommitDevice(rStepAct);
                        }
                        catch (Exception exception)
                        {

                        }
                    }));

            comitThread.Start();
        }

    }
}
