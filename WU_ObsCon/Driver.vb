' --------------------------------------------------------------------------------
' ASCOM ObservingConditions driver for WUAPI
'
' Description:	ASCOM Observing Conditions driver which utilizes the Weather Underground
'				API to collect current observations from a user selected station.
'				Users will provide their own API key,
'
' Implements:	ASCOM ObservingConditions interface version: 1.0
' Author:		(EOR) EorEquis@tristarobservatory.space
'
' Edit Log:
'
' Date			Who	Vers	Description
' -----------	---	-----	-------------------------------------------------------
' 15-JAN-2018	EOR	1.0.0	Initial edit, from ObservingConditions template
' 16-JAN-2018	EOR	1.0.0	More work
' 17-JAN-2018   EOR 1.1.0   Change to undocumented WU XML API, because the "real" API deliveres eroneous data, and WU won't even acknowledge this.
'                           This also means no API key is needed.
' ---------------------------------------------------------------------------------
'
' Your driver's ID is ASCOM.Wunderground.ObservingConditions
'
' The Guid attribute sets the CLSID for ASCOM.DeviceName.ObservingConditions
' The ClassInterface/None addribute prevents an empty interface called
' _ObservingConditions from being created and used as the [default] interface
'

' This definition is used to select code that's only applicable for one device type
#Const Device = "ObservingConditions"

Imports ASCOM
Imports ASCOM.DeviceInterface
Imports ASCOM.Utilities

Imports System
Imports System.Collections
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Net
Imports System.IO
Imports System.Xml


<Guid("3a09929a-30c1-4987-9d18-8c38476bdb43")> _
<ClassInterface(ClassInterfaceType.None)> _
Public Class ObservingConditions

    ' The Guid attribute sets the CLSID for ASCOM.Wunderground.ObservingConditions
    ' The ClassInterface/None addribute prevents an empty interface called
    ' _Wunderground from being created and used as the [default] interface

    ' TODO Replace the not implemented exceptions with code to implement the function or
    ' throw the appropriate ASCOM exception.
    '
    Implements IObservingConditions

    '
    ' Driver ID and descriptive string that shows in the Chooser
    '
    Friend Shared driverID As String = "ASCOM.Wunderground.ObservingConditions"
    Private Shared driverDescription As String = "Wunderground ObservingConditions"

    Friend Shared mutex As New System.Threading.Mutex()
    Friend Shared LastUpdate As DateTime = "1/1/1970"
    Friend Shared IsUpdated As Boolean = False
    Friend Shared varDewPoint As Double = 0.0
    Friend Shared varHumidity As Double = 0.0
    Friend Shared varPressure As Double = 0.0
    Friend Shared varRainRate As Double = 0.0
    Friend Shared varTemperature As Double = 0.0
    Friend Shared varWindDirection As Double = 0.0
    Friend Shared varWindGust As Double = 0.0
    Friend Shared varWindSpeed As Double = 0.0
    Friend Shared varTimeSinceLastUpdate As Double = 0.0

    Friend Shared comPortProfileName As String = "COM Port" 'Constants used for Profile persistence
    Friend Shared traceStateProfileName As String = "Trace Level"
    Friend Shared comPortDefault As String = "COM1"
    Friend Shared traceStateDefault As String = "False"
    Friend Shared APIKeyProfileName As String = "APIKey"
    Friend Shared APIKeyDefault As String = ""
    Friend Shared StationIDProfileName As String = "StationID"
    Friend Shared StationIDDefault As String = ""
    Friend Shared AvgPer As Double = 0.0

    Friend Shared traceState As Boolean
    Friend Shared StationID As String
    Friend Shared APIKey As String

    Private connectedState As Boolean ' Private variable to hold the connected state
    Private utilities As Util ' Private variable to hold an ASCOM Utilities object
    Private TL As TraceLogger ' Private variable to hold the trace logger object (creates a diagnostic log file with information that you specify)

    '
    ' Constructor - Must be public for COM registration!
    '
    Public Sub New()

        ReadProfile() ' Read device configuration from the ASCOM Profile store
        TL = New TraceLogger("", "Wunderground")
        TL.Enabled = traceState

        TL.LogMessage("ObservingConditions", "Starting initialisation")

        connectedState = False ' Initialise connected to false
        utilities = New Util() ' Initialise util object

        TL.LogMessage("ObservingConditions", "Completed initialisation")
    End Sub

    '
    ' PUBLIC COM INTERFACE IObservingConditions IMPLEMENTATION
    '

#Region "Common properties and methods"
    ''' <summary>
    ''' Displays the Setup Dialog form.
    ''' If the user clicks the OK button to dismiss the form, then
    ''' the new settings are saved, otherwise the old values are reloaded.
    ''' THIS IS THE ONLY PLACE WHERE SHOWING USER INTERFACE IS ALLOWED!
    ''' </summary>
    Public Sub SetupDialog() Implements IObservingConditions.SetupDialog
        ' consider only showing the setup dialog if not connected
        ' or call a different dialog if connected
        If IsConnected Then
            System.Windows.Forms.MessageBox.Show("Already connected, just press OK")
        End If

        Using F As SetupDialogForm = New SetupDialogForm()
            Dim result As System.Windows.Forms.DialogResult = F.ShowDialog()
            If result = DialogResult.OK Then
                WriteProfile() ' Persist device configuration values to the ASCOM Profile store
            End If
        End Using
    End Sub

    Public ReadOnly Property SupportedActions() As ArrayList Implements IObservingConditions.SupportedActions
        Get
            TL.LogMessage("SupportedActions Get", "Returning empty arraylist")
            Return New ArrayList()
        End Get
    End Property

    Public Function Action(ByVal ActionName As String, ByVal ActionParameters As String) As String Implements IObservingConditions.Action
        Throw New ActionNotImplementedException("Action " & ActionName & " is not supported by this driver")
    End Function

    Public Sub CommandBlind(ByVal Command As String, Optional ByVal Raw As Boolean = False) Implements IObservingConditions.CommandBlind
        Throw New MethodNotImplementedException("CommandBlind")
    End Sub

    Public Function CommandBool(ByVal Command As String, Optional ByVal Raw As Boolean = False) As Boolean _
        Implements IObservingConditions.CommandBool
        CheckConnected("CommandBool")
        mutex.WaitOne()
        Try
            Return DoUpdate()
        Catch ex As Exception
        Finally
            mutex.ReleaseMutex()
        End Try
    End Function

    Public Function CommandString(ByVal Command As String, Optional ByVal Raw As Boolean = False) As String _
        Implements IObservingConditions.CommandString
        Throw New MethodNotImplementedException("CommandBool")
    End Function

    Public Function DoUpdate() As Boolean
        If (DateTime.Now - LastUpdate).TotalMilliseconds > 30000 Then
            Dim wxXML As XmlDocument = GetXML()

            varDewPoint = CDbl(wxXML.SelectSingleNode("//current_observation/dewpoint_c").InnerText)
            varHumidity = CDbl(wxXML.SelectSingleNode("//current_observation/relative_humidity").InnerText)
            varPressure = CDbl(wxXML.SelectSingleNode("//current_observation/pressure_mb").InnerText)
            varRainRate = CDbl(wxXML.SelectSingleNode("//current_observation/precip_1hr_metric").InnerText)
            varTemperature = CDbl(wxXML.SelectSingleNode("//current_observation/temp_c").InnerText)
            varWindDirection = CDbl(wxXML.SelectSingleNode("//current_observation/wind_degrees").InnerText)
            varWindGust = CDbl(wxXML.SelectSingleNode("//current_observation/wind_gust_mph").InnerText)
            varWindGust = varWindGust * 0.44704     'Convert from mph to mps
            varWindSpeed = CDbl(wxXML.SelectSingleNode("//current_observation/wind_mph").InnerText)
            varWindSpeed = varWindSpeed * 0.44704     'Convert from mph to mps
            LastUpdate = DateTime.Now
            Return True
        Else
            Return False
        End If

    End Function

    Public Function GetXML() As XmlDocument

        Dim URL As String = "http://api.wunderground.com/weatherstation/WXCurrentObXML.asp?ID=" & StationID
        Dim reader As New XmlTextReader(URL)
        Dim document As New XmlDocument
        document.Load(reader)
        Return document

    End Function

    Public Property Connected() As Boolean Implements IObservingConditions.Connected
        Get
            TL.LogMessage("Connected Get", IsConnected.ToString())
            Return IsConnected
        End Get
        Set(value As Boolean)
            TL.LogMessage("Connected Set", value.ToString())
            If value = IsConnected Then
                Return
            End If

            If value Then

                Dim wxXML As XmlDocument = GetXML()
                Dim strError As String = wxXML.SelectSingleNode("//current_observation/station_id").InnerText
                If strError = StationID Then
                    connectedState = True
                    TL.LogMessage("Connected Set", "Valid ID " + StationID + " and key " + APIKey)
                Else
                    TL.LogMessage("Connected Set", "Error connecting to API")
                End If

            Else
                connectedState = False
                TL.LogMessage("Connected Set", "Disconnected")
            End If
        End Set
    End Property

    Public ReadOnly Property Description As String Implements IObservingConditions.Description
        Get
            ' this pattern seems to be needed to allow a public property to return a private field
            Dim d As String = driverDescription
            TL.LogMessage("Description Get", d)
            Return d
        End Get
    End Property

    Public ReadOnly Property DriverInfo As String Implements IObservingConditions.DriverInfo
        Get
            Dim m_version As Version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version
            ' TODO customise this driver description
            Dim s_driverInfo As String = "Information about the driver itself. Version: " + m_version.Major.ToString() + "." + m_version.Minor.ToString()
            TL.LogMessage("DriverInfo Get", s_driverInfo)
            Return s_driverInfo
        End Get
    End Property

    Public ReadOnly Property DriverVersion() As String Implements IObservingConditions.DriverVersion
        Get
            ' Get our own assembly and report its version number
            TL.LogMessage("DriverVersion Get", Reflection.Assembly.GetExecutingAssembly.GetName.Version.ToString(2))
            Return Reflection.Assembly.GetExecutingAssembly.GetName.Version.ToString(2)
        End Get
    End Property

    Public ReadOnly Property InterfaceVersion() As Short Implements IObservingConditions.InterfaceVersion
        Get
            TL.LogMessage("InterfaceVersion Get", "1")
            Return 1
        End Get
    End Property

    Public ReadOnly Property Name As String Implements IObservingConditions.Name
        Get
            Dim s_name As String = "WUAPI ObsCon"
            TL.LogMessage("Name Get", s_name)
            Return s_name
        End Get
    End Property

    Public Sub Dispose() Implements IObservingConditions.Dispose
        ' Clean up the tracelogger and util objects
        TL.Enabled = False
        TL.Dispose()
        TL = Nothing
        utilities.Dispose()
        utilities = Nothing
    End Sub

#End Region

#Region "IObservingConditions Implementation"

    Public Property AveragePeriod() As Double Implements IObservingConditions.AveragePeriod
        Get
            TL.LogMessage("AveragePeriod", AvgPer.ToString())
            Return AvgPer
        End Get
        Set(value As Double)
            If value < 0 Then
                Throw New ASCOM.InvalidValueException("Value cannot be below 0")
                TL.LogMessage("Average Period", "Error - Invalid Value : " + value.ToString)
            Else
                TL.LogMessage("AveragePeriod", "Set to " + value.ToString)
                AvgPer = value
            End If
        End Set
    End Property

    Public ReadOnly Property CloudCover() As Double Implements IObservingConditions.CloudCover
        Get
            TL.LogMessage("CloudCover", "Get Not implemented")
            Throw New ASCOM.PropertyNotImplementedException("CloudCover", False)
        End Get
    End Property

    Public ReadOnly Property DewPoint() As Double Implements IObservingConditions.DewPoint
        Get
            IsUpdated = CommandBool("")
            TL.LogMessage("DewPoint", varDewPoint)
            Return varDewPoint
        End Get
    End Property

    Public ReadOnly Property Humidity() As Double Implements IObservingConditions.Humidity
        Get
            IsUpdated = CommandBool("")
            TL.LogMessage("Humidity", varHumidity)
            Return varHumidity
        End Get
    End Property

    Public ReadOnly Property Pressure() As Double Implements IObservingConditions.Pressure
        Get
            IsUpdated = CommandBool("")
            TL.LogMessage("Pressure", varPressure)
            Return varPressure
        End Get
    End Property

    Public ReadOnly Property RainRate() As Double Implements IObservingConditions.RainRate
        Get
            IsUpdated = CommandBool("")
            TL.LogMessage("RainRate", varRainRate)
            Return varRainRate
        End Get
    End Property

    Public ReadOnly Property SkyBrightness() As Double Implements IObservingConditions.SkyBrightness
        Get
            TL.LogMessage("SkyBrightness", "Get Not implemented")
            Throw New ASCOM.PropertyNotImplementedException("SkyBrightness", False)
        End Get
    End Property

    Public ReadOnly Property SkyQuality() As Double Implements IObservingConditions.SkyQuality
        Get
            TL.LogMessage("SkyQuality", "Get Not implemented")
            Throw New ASCOM.PropertyNotImplementedException("SkyQuality", False)
        End Get
    End Property

    Public ReadOnly Property StarFWHM() As Double Implements IObservingConditions.StarFWHM
        Get
            TL.LogMessage("StarFWHM", "Get Not implemented")
            Throw New ASCOM.PropertyNotImplementedException("StarFWHM", False)
        End Get
    End Property

    Public ReadOnly Property SkyTemperature() As Double Implements IObservingConditions.SkyTemperature
        Get
            TL.LogMessage("SkyTemperature", "Get Not implemented")
            Throw New ASCOM.PropertyNotImplementedException("SkyTemperature", False)
        End Get
    End Property

    Public ReadOnly Property Temperature() As Double Implements IObservingConditions.Temperature
        Get
            TL.LogMessage("Temperature", varTemperature)
            IsUpdated = CommandBool("")
            Return varTemperature
        End Get
    End Property

    Public ReadOnly Property WindDirection() As Double Implements IObservingConditions.WindDirection
        Get
            IsUpdated = CommandBool("")
            TL.LogMessage("WindDirection", varWindDirection)
            Return varWindDirection
        End Get
    End Property

    Public ReadOnly Property WindGust() As Double Implements IObservingConditions.WindGust
        Get
            TL.LogMessage("WindGust", varWindGust)
            IsUpdated = CommandBool("")
            Return varWindGust
        End Get
    End Property

    Public ReadOnly Property WindSpeed() As Double Implements IObservingConditions.WindSpeed
        Get
            IsUpdated = CommandBool("")
            TL.LogMessage("WindSpeed", varWindSpeed)
            Return varWindSpeed
        End Get
    End Property

    Public Function TimeSinceLastUpdate(PropertyName As String) As Double Implements IObservingConditions.TimeSinceLastUpdate
        varTimeSinceLastUpdate = (DateTime.Now - LastUpdate).TotalSeconds
        Select Case PropertyName.Trim.ToLowerInvariant
            Case ""
                TL.LogMessage("TimeSinceLastUpdate", "Latest : " & varTimeSinceLastUpdate.ToString)
                Return TimeSinceLastUpdate
            Case "cloudcover"
                TL.LogMessage("TimeSinceLastUpdate", PropertyName & " - not implemented")
                Throw New MethodNotImplementedException("TimeSinceLastUpdate(" + PropertyName + ")")
            Case "dewpoint"
                TL.LogMessage("TimeSinceLastUpdate", PropertyName & " : " & varTimeSinceLastUpdate.ToString)
                Return TimeSinceLastUpdate
            Case "humidity"
                TL.LogMessage("TimeSinceLastUpdate", PropertyName & " : " & varTimeSinceLastUpdate.ToString)
                Return TimeSinceLastUpdate
            Case "pressure"
                TL.LogMessage("TimeSinceLastUpdate", PropertyName & " : " & varTimeSinceLastUpdate.ToString)
                Return TimeSinceLastUpdate
            Case "rainrate"
                TL.LogMessage("TimeSinceLastUpdate", PropertyName & " : " & varTimeSinceLastUpdate.ToString)
                Return TimeSinceLastUpdate
            Case "skybrightness"
                TL.LogMessage("TimeSinceLastUpdate", PropertyName & " - not implemented")
                Throw New MethodNotImplementedException("TimeSinceLastUpdate(" + PropertyName + ")")
            Case "skyquality"
                TL.LogMessage("TimeSinceLastUpdate", PropertyName & " - not implemented")
                Throw New MethodNotImplementedException("TimeSinceLastUpdate(" + PropertyName + ")")
            Case "starfwhm"
                TL.LogMessage("TimeSinceLastUpdate", PropertyName & " - not implemented")
                Throw New MethodNotImplementedException("TimeSinceLastUpdate(" + PropertyName + ")")
            Case "skytemperature"
                TL.LogMessage("TimeSinceLastUpdate", PropertyName & " - not implemented")
                Throw New MethodNotImplementedException("TimeSinceLastUpdate(" + PropertyName + ")")
            Case "temperature"
                TL.LogMessage("TimeSinceLastUpdate", PropertyName & " : " & varTimeSinceLastUpdate.ToString)
                Return TimeSinceLastUpdate
            Case "winddirection"
                TL.LogMessage("TimeSinceLastUpdate", PropertyName & " : " & varTimeSinceLastUpdate.ToString)
                Return TimeSinceLastUpdate
            Case "windgust"
                TL.LogMessage("TimeSinceLastUpdate", PropertyName & " : " & varTimeSinceLastUpdate.ToString)
                Return TimeSinceLastUpdate
            Case "windspeed"
                TL.LogMessage("TimeSinceLastUpdate", PropertyName & " : " & varTimeSinceLastUpdate.ToString)
                Return TimeSinceLastUpdate

        End Select
        TL.LogMessage("TimeSinceLastUpdate", PropertyName & " - unrecognised")
        Throw New ASCOM.InvalidValueException("TimeSinceLastUpdate(" + PropertyName + ")")

    End Function

    Public Function SensorDescription(PropertyName As String) As String Implements IObservingConditions.SensorDescription
        Select Case PropertyName.Trim.ToLowerInvariant
            Case "averageperiod"
                Return "Average period in hours, immediate values are only available"
            Case "cloudcover"
                TL.LogMessage("SensorDescription", PropertyName & " - not implemented")
                Throw New MethodNotImplementedException("SensorDescription(" + PropertyName + ")")
            Case "dewpoint"
                Return "Atmospheric dew point reported in °C."
            Case "humidity"
                Return "Atmospheric humidity (%)"
            Case "pressure"
                Return "Relative atmospheric presure at the observatory (hPa)"
            Case "rainrate"
                Return "Rain rate (mm / hour)"
            Case "skybrightness"
                TL.LogMessage("SensorDescription", PropertyName & " - not implemented")
                Throw New MethodNotImplementedException("SensorDescription(" + PropertyName + ")")
            Case "skyquality"
                TL.LogMessage("SensorDescription", PropertyName & " - not implemented")
                Throw New MethodNotImplementedException("SensorDescription(" + PropertyName + ")")
            Case "starfwhm"
                TL.LogMessage("SensorDescription", PropertyName & " - not implemented")
                Throw New MethodNotImplementedException("SensorDescription(" + PropertyName + ")")
            Case "skytemperature"
                TL.LogMessage("SensorDescription", PropertyName & " - not implemented")
                Throw New MethodNotImplementedException("SensorDescription(" + PropertyName + ")")
            Case "temperature"
                Return "Temperature in °C"
            Case "winddirection"
                Return "Wind direction (degrees, 0..360.0)"
            Case "windgust"
                Return "Wind gust (m/s) Peak 3 second wind speed over the last ?? minutes" ' -- TODO : Find out how WU averages this
            Case "windspeed"
                Return "Wind speed (m/s)"

        End Select
        TL.LogMessage("SensorDescription", PropertyName & " - unrecognised")
        Throw New ASCOM.InvalidValueException("SensorDescription(" + PropertyName + ")")
    End Function

    Public Sub Refresh() Implements IObservingConditions.Refresh
        TL.LogMessage("Refresh", "DoUpdate() called")
        IsUpdated = CommandBool("")
    End Sub

#End Region

#Region "Private properties and methods"
    ' here are some useful properties and methods that can be used as required
    ' to help with

#Region "ASCOM Registration"

    Private Shared Sub RegUnregASCOM(ByVal bRegister As Boolean)

        Using P As New Profile() With {.DeviceType = "ObservingConditions"}
            If bRegister Then
                P.Register(driverID, driverDescription)
            Else
                P.Unregister(driverID)
            End If
        End Using

    End Sub

    <ComRegisterFunction()> _
    Public Shared Sub RegisterASCOM(ByVal T As Type)

        RegUnregASCOM(True)

    End Sub

    <ComUnregisterFunction()> _
    Public Shared Sub UnregisterASCOM(ByVal T As Type)

        RegUnregASCOM(False)

    End Sub

#End Region

    ''' <summary>
    ''' Returns true if there is a valid connection to the driver hardware
    ''' </summary>
    Private ReadOnly Property IsConnected As Boolean
        Get
            Return connectedState
        End Get
    End Property

    ''' <summary>
    ''' Use this function to throw an exception if we aren't connected to the hardware
    ''' </summary>
    ''' <param name="message"></param>
    Private Sub CheckConnected(ByVal message As String)
        If Not IsConnected Then
            Throw New NotConnectedException(message)
        End If
    End Sub

    ''' <summary>
    ''' Read the device configuration from the ASCOM Profile store
    ''' </summary>
    Friend Sub ReadProfile()
        Using driverProfile As New Profile()
            driverProfile.DeviceType = "ObservingConditions"
            traceState = Convert.ToBoolean(driverProfile.GetValue(driverID, traceStateProfileName, String.Empty, traceStateDefault))
            APIKey = driverProfile.GetValue(driverID, APIKeyProfileName, String.Empty, APIKeyDefault)
            StationID = driverProfile.GetValue(driverID, StationIDProfileName, String.Empty, StationIDDefault)
        End Using
    End Sub

    ''' <summary>
    ''' Write the device configuration to the  ASCOM  Profile store
    ''' </summary>
    Friend Sub WriteProfile()
        Using driverProfile As New Profile()
            driverProfile.DeviceType = "ObservingConditions"
            driverProfile.WriteValue(driverID, traceStateProfileName, traceState.ToString())
            driverProfile.WriteValue(driverID, APIKeyProfileName, APIKey)
            driverProfile.WriteValue(driverID, StationIDProfileName, StationID)
        End Using

    End Sub

#End Region

End Class
