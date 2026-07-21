Imports System.Numerics
Imports System.Security.Cryptography

Public Module Pcl2TaowaCore

    Private Const TerracottaVersion As String = "0.4.2"
    Private Const EasyTierVersion As String = "v2.5.0-terracotta.2"
    Private Const RoomCodeLength As Integer = 21
    Private ReadOnly RoomChars As Char() = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ".ToCharArray()
    Private ReadOnly RoomValueLimit As BigInteger = BigInteger.Pow(New BigInteger(34), 16)

    Private ReadOnly StateLock As New Object
    Private CurrentState As TaowaAppState = TaowaAppState.CreateWaiting()
    Private CurrentStateIndex As Integer = 0
    Private CurrentSharingIndex As Integer = 0
    Private CachedMachineId As String = Nothing

#Region "Room"

    Public Function CreateRoom() As TaowaRoom
        Dim Bytes(15) As Byte
        Using Rng = RandomNumberGenerator.Create()
            Rng.GetBytes(Bytes)
        End Using

        Dim Value As BigInteger = BigInteger.Zero
        For Each Item In Bytes
            Value = Value * 256 + Item
        Next
        Value = BigInteger.Remainder(Value, RoomValueLimit)
        Value -= BigInteger.Remainder(Value, New BigInteger(7))
        Return RoomFromValue(Value)
    End Function

    Public Function ParseRoom(Code As String) As TaowaRoom
        If String.IsNullOrEmpty(Code) Then Return Nothing
        Dim Raw = Code.ToUpperInvariant()
        If Raw.Length < RoomCodeLength Then Return Nothing

        For Start = 0 To Raw.Length - RoomCodeLength
            If Raw(Start) <> "U"c OrElse Raw(Start + 1) <> "/"c Then Continue For

            Dim Value As BigInteger = BigInteger.Zero
            Dim Success = True
            For i = "XXXX-XXXX-XXXX-XXXX".Length - 1 To 0 Step -1
                Dim C = Raw(Start + 2 + i)
                If i = 4 OrElse i = 9 OrElse i = 14 Then
                    If C <> "-"c Then
                        Success = False
                        Exit For
                    End If
                Else
                    Dim Digit = LookupRoomChar(C)
                    If Digit < 0 Then
                        Success = False
                        Exit For
                    End If
                    Value = Value * 34 + Digit
                End If
            Next

            If Success AndAlso BigInteger.Remainder(Value, New BigInteger(7)) = BigInteger.Zero Then Return RoomFromValue(Value)
        Next
        Return Nothing
    End Function

    Private Function LookupRoomChar(Value As Char) As Integer
        Select Case Value
            Case "I"c
                Value = "1"c
            Case "O"c
                Value = "0"c
        End Select
        For i = 0 To RoomChars.Length - 1
            If RoomChars(i) = Value Then Return i
        Next
        Return -1
    End Function

    Private Function RoomFromValue(Value As BigInteger) As TaowaRoom
        Dim Code As New StringBuilder("U/")
        Dim NetworkName As New StringBuilder("scaffolding-mc-")
        Dim NetworkSecret As New StringBuilder

        For i = 0 To 15
            Dim Digit = CInt(BigInteger.Remainder(Value, New BigInteger(34)))
            Value = BigInteger.Divide(Value, New BigInteger(34))
            Dim C = RoomChars(Digit)

            If i = 4 OrElse i = 8 OrElse i = 12 Then Code.Append("-"c)
            Code.Append(C)

            If i < 8 Then
                If i = 4 Then NetworkName.Append("-"c)
                NetworkName.Append(C)
            Else
                If i = 12 Then NetworkSecret.Append("-"c)
                NetworkSecret.Append(C)
            End If
        Next

        Return New TaowaRoom(Code.ToString(), NetworkName.ToString(), NetworkSecret.ToString())
    End Function

#End Region

#Region "State"

    Public Function GetStateJson() As JObject
        SyncLock StateLock
            Return CurrentState.ToJson(CurrentStateIndex, CurrentSharingIndex)
        End SyncLock
    End Function

    Public Function GetStateRaw() As String
        Return GetStateJson().ToString(Newtonsoft.Json.Formatting.None)
    End Function

    Public Function GetStateSnapshot() As TaowaStateSnapshot
        SyncLock StateLock
            Return New TaowaStateSnapshot(CurrentState.Clone(), CurrentStateIndex, CurrentSharingIndex)
        End SyncLock
    End Function

    Public Function SetWaiting() As TaowaStateCapture
        SyncLock StateLock
            If CurrentState.Kind = TaowaAppStateKind.Waiting Then Return New TaowaStateCapture(CurrentStateIndex)
            Return SetStateLocked(TaowaAppState.CreateWaiting())
        End SyncLock
    End Function

    Public Function SetHostScanning(Scanner As TaowaMinecraftScanner) As TaowaStateCapture
        SyncLock StateLock
            If CurrentState.Kind <> TaowaAppStateKind.Waiting Then Return Nothing
            Return SetStateLocked(TaowaAppState.CreateHostScanning(Scanner))
        End SyncLock
    End Function

    Public Function SetHostStarting(Room As TaowaRoom, Port As Integer, Capture As TaowaStateCapture) As TaowaStateCapture
        SyncLock StateLock
            If Not CanCaptureLocked(Capture) Then Return Nothing
            Return SetStateLocked(TaowaAppState.CreateHostStarting(Room, Port))
        End SyncLock
    End Function

    Public Function SetHostOk(Room As TaowaRoom, Port As Integer, Profiles As IEnumerable(Of TaowaProfile), Capture As TaowaStateCapture) As TaowaStateCapture
        SyncLock StateLock
            If Not CanCaptureLocked(Capture) Then Return Nothing
            Return SetStateLocked(TaowaAppState.CreateHostOk(Room, Port, Profiles))
        End SyncLock
    End Function

    Public Function SetGuestConnecting(Room As TaowaRoom) As TaowaStateCapture
        SyncLock StateLock
            If CurrentState.Kind <> TaowaAppStateKind.Waiting Then Return Nothing
            Return SetStateLocked(TaowaAppState.CreateGuestConnecting(Room))
        End SyncLock
    End Function

    Public Function SetGuestStarting(Room As TaowaRoom, Difficulty As TaowaConnectionDifficulty, Capture As TaowaStateCapture) As TaowaStateCapture
        SyncLock StateLock
            If Not CanCaptureLocked(Capture) Then Return Nothing
            Return SetStateLocked(TaowaAppState.CreateGuestStarting(Room, Difficulty))
        End SyncLock
    End Function

    Public Function SetGuestOk(Room As TaowaRoom, ServerPort As Integer, Profiles As IEnumerable(Of TaowaProfile), Capture As TaowaStateCapture) As TaowaStateCapture
        SyncLock StateLock
            If Not CanCaptureLocked(Capture) Then Return Nothing
            Return SetStateLocked(TaowaAppState.CreateGuestOk(Room, ServerPort, Profiles))
        End SyncLock
    End Function

    Public Function SetException(Kind As TaowaExceptionType, Capture As TaowaStateCapture) As TaowaStateCapture
        SyncLock StateLock
            If Capture IsNot Nothing AndAlso Not CanCaptureLocked(Capture) Then Return Nothing
            Return SetStateLocked(TaowaAppState.CreateException(Kind))
        End SyncLock
    End Function

    Public Sub IncreaseShared()
        SyncLock StateLock
            CurrentStateIndex += 1
            CurrentSharingIndex += 1
        End SyncLock
    End Sub

    Public Function CanCapture(Capture As TaowaStateCapture) As Boolean
        SyncLock StateLock
            Return CanCaptureLocked(Capture)
        End SyncLock
    End Function

    Private Function SetStateLocked(State As TaowaAppState) As TaowaStateCapture
        CurrentState = State
        CurrentStateIndex += 1
        CurrentSharingIndex = 0
        Log("TaowaCore state -> " & State.Kind.ToString())
        Return New TaowaStateCapture(CurrentStateIndex)
    End Function

    Private Function CanCaptureLocked(Capture As TaowaStateCapture) As Boolean
        If Capture Is Nothing Then Return False
        Return CurrentStateIndex - CurrentSharingIndex <= Capture.Index
    End Function

#End Region

#Region "Profile"

    Public Function GetMachineId() As String
        If CachedMachineId IsNot Nothing Then Return CachedMachineId
        SyncLock StateLock
            If CachedMachineId IsNot Nothing Then Return CachedMachineId

            Dim MachineFile = PathTemp & "Taowa\machine-id"
            Try
                DirectoryUtils.Create(Path.GetDirectoryName(MachineFile))
                If File.Exists(MachineFile) Then
                    Dim Existing = File.ReadAllBytes(MachineFile)
                    If Existing.Length = 16 Then
                        CachedMachineId = BytesToHex(Existing)
                        Return CachedMachineId
                    End If
                End If

                Dim Bytes(15) As Byte
                Using Rng = RandomNumberGenerator.Create()
                    Rng.GetBytes(Bytes)
                End Using
                File.WriteAllBytes(MachineFile, Bytes)
                CachedMachineId = BytesToHex(Bytes)
                Return CachedMachineId
            Catch
                Dim Bytes(15) As Byte
                Using Rng = RandomNumberGenerator.Create()
                    Rng.GetBytes(Bytes)
                End Using
                CachedMachineId = BytesToHex(Bytes)
                Return CachedMachineId
            End Try
        End SyncLock
    End Function

    Public Function GetVendor() As String
        Return $"Terracotta {TerracottaVersion}, EasyTier {EasyTierVersion}"
    End Function

    Public Function CreateLocalProfile(Name As String, Kind As TaowaProfileKind) As TaowaProfile
        Return New TaowaProfile(GetMachineId(), If(String.IsNullOrWhiteSpace(Name), "Player", Name), GetVendor(), Kind)
    End Function

    Private Function BytesToHex(Bytes As Byte()) As String
        Dim Builder As New StringBuilder(Bytes.Length * 2)
        For Each Item In Bytes
            Builder.Append(Item.ToString("x2"))
        Next
        Return Builder.ToString()
    End Function

#End Region

#Region "Scaffolding packets"

    Public Function CreateScaffoldingRequest(NamespaceName As String, PathName As String, Body As Byte()) As Byte()
        Dim Kind = Encoding.UTF8.GetBytes(NamespaceName & ":" & PathName)
        If Kind.Length > 255 Then Throw New ArgumentException("Scaffolding request kind is too long")
        If Body Is Nothing Then Body = New Byte() {}

        Dim Result As New List(Of Byte)(1 + Kind.Length + 4 + Body.Length)
        Result.Add(CByte(Kind.Length))
        Result.AddRange(Kind)
        AddUInt32BigEndian(Result, CUInt(Body.Length))
        Result.AddRange(Body)
        Return Result.ToArray()
    End Function

    Public Function ParseScaffoldingResponse(Data As Byte()) As TaowaPacketResponse
        If Data Is Nothing OrElse Data.Length < 5 Then Return Nothing
        Dim Status = Data(0)
        Dim Length = (CInt(Data(1)) << 24) Or (CInt(Data(2)) << 16) Or (CInt(Data(3)) << 8) Or CInt(Data(4))
        If Length < 0 OrElse Data.Length - 5 < Length Then Return Nothing
        Dim Body As Byte()
        If Length > 0 Then
            Body = New Byte(Length - 1) {}
            Array.Copy(Data, 5, Body, 0, Length)
        Else
            Body = New Byte() {}
        End If
        Return New TaowaPacketResponse(Status, Body)
    End Function

    Private Sub AddUInt32BigEndian(Target As List(Of Byte), Value As UInteger)
        Target.Add(CByte((Value >> 24) And &HFFUI))
        Target.Add(CByte((Value >> 16) And &HFFUI))
        Target.Add(CByte((Value >> 8) And &HFFUI))
        Target.Add(CByte(Value And &HFFUI))
    End Sub

#End Region

    Private Sub Log(Message As String)
        Try
            Dim LogFolder = Path.Combine(If(AppDomain.CurrentDomain.BaseDirectory, "."), "PCL")
            DirectoryUtils.Create(LogFolder)
            File.AppendAllText(Path.Combine(LogFolder, "TaowaCore.log"), Date.Now.ToString("HH:mm:ss.fff") & " " & Message & vbCrLf, Encoding.UTF8)
        Catch
        End Try
    End Sub

End Module

Public Enum TaowaProfileKind
    HOST
    LOCAL
    GUEST
End Enum

Public Enum TaowaConnectionDifficulty
    Unknown
    Easiest
    Simple
    Medium
    Tough
End Enum

Public Enum TaowaExceptionType
    PingHostFail = 0
    PingHostRst = 1
    GuestEasytierCrash = 2
    HostEasytierCrash = 3
    PingServerRst = 4
    ScaffoldingInvalidResponse = 5
End Enum

Public Enum TaowaAppStateKind
    Waiting
    HostScanning
    HostStarting
    HostOk
    GuestConnecting
    GuestStarting
    GuestOk
    Exception
End Enum

Public Class TaowaRoom
    Public ReadOnly Property Code As String
    Public ReadOnly Property NetworkName As String
    Public ReadOnly Property NetworkSecret As String

    Public Sub New(Code As String, NetworkName As String, NetworkSecret As String)
        Me.Code = Code
        Me.NetworkName = NetworkName
        Me.NetworkSecret = NetworkSecret
    End Sub
End Class

Public Class TaowaProfile
    Public Property MachineId As String
    Public Property Name As String
    Public Property Vendor As String
    Public Property Kind As TaowaProfileKind

    Public Sub New(MachineId As String, Name As String, Vendor As String, Kind As TaowaProfileKind)
        Me.MachineId = MachineId
        Me.Name = Name
        Me.Vendor = Vendor
        Me.Kind = Kind
    End Sub

    Public Function Clone() As TaowaProfile
        Return New TaowaProfile(MachineId, Name, Vendor, Kind)
    End Function

    Public Function ToJson() As JObject
        Return New JObject(
            New JProperty("machine_id", MachineId),
            New JProperty("name", Name),
            New JProperty("vendor", Vendor),
            New JProperty("kind", Kind.ToString()))
    End Function
End Class

Public Class TaowaTimedProfile
    Public Property UpdatedAt As Date
    Public Property Profile As TaowaProfile

    Public Sub New(Profile As TaowaProfile, Optional UpdatedAt As Date = Nothing)
        Me.Profile = Profile
        Me.UpdatedAt = If(UpdatedAt = Nothing, Date.UtcNow, UpdatedAt)
    End Sub

    Public Function Clone() As TaowaTimedProfile
        Return New TaowaTimedProfile(Profile.Clone(), UpdatedAt)
    End Function
End Class

Public Class TaowaStateCapture
    Public ReadOnly Property Index As Integer

    Public Sub New(Index As Integer)
        Me.Index = Index
    End Sub
End Class

Public Class TaowaStateSnapshot
    Public ReadOnly Property State As TaowaAppState
    Public ReadOnly Property Index As Integer
    Public ReadOnly Property SharingIndex As Integer

    Public Sub New(State As TaowaAppState, Index As Integer, SharingIndex As Integer)
        Me.State = State
        Me.Index = Index
        Me.SharingIndex = SharingIndex
    End Sub
End Class

Public Class TaowaPacketResponse
    Public ReadOnly Property Status As Byte
    Public ReadOnly Property Data As Byte()

    Public Sub New(Status As Byte, Data As Byte())
        Me.Status = Status
        Me.Data = If(Data, New Byte() {})
    End Sub

    Public ReadOnly Property IsOk As Boolean
        Get
            Return Status = 0
        End Get
    End Property
End Class

Public Class TaowaAppState
    Public Property Kind As TaowaAppStateKind
    Public Property Room As TaowaRoom
    Public Property Port As Integer
    Public Property ServerPort As Integer
    Public Property Difficulty As TaowaConnectionDifficulty = TaowaConnectionDifficulty.Unknown
    Public Property ExceptionKind As TaowaExceptionType
    Public Property Scanner As TaowaMinecraftScanner
    Public Property HostProfiles As List(Of TaowaTimedProfile)
    Public Property Profiles As List(Of TaowaProfile)

    Private Sub New(Kind As TaowaAppStateKind)
        Me.Kind = Kind
    End Sub

    Public Shared Function CreateWaiting() As TaowaAppState
        Return New TaowaAppState(TaowaAppStateKind.Waiting)
    End Function

    Public Shared Function CreateHostScanning(Scanner As TaowaMinecraftScanner) As TaowaAppState
        Return New TaowaAppState(TaowaAppStateKind.HostScanning) With {.Scanner = Scanner}
    End Function

    Public Shared Function CreateHostStarting(Room As TaowaRoom, Port As Integer) As TaowaAppState
        Return New TaowaAppState(TaowaAppStateKind.HostStarting) With {.Room = Room, .Port = Port}
    End Function

    Public Shared Function CreateHostOk(Room As TaowaRoom, Port As Integer, Profiles As IEnumerable(Of TaowaProfile)) As TaowaAppState
        Return New TaowaAppState(TaowaAppStateKind.HostOk) With {
            .Room = Room,
            .Port = Port,
            .HostProfiles = If(Profiles, New List(Of TaowaProfile)).Select(Function(p) New TaowaTimedProfile(p.Clone())).ToList()
        }
    End Function

    Public Shared Function CreateGuestConnecting(Room As TaowaRoom) As TaowaAppState
        Return New TaowaAppState(TaowaAppStateKind.GuestConnecting) With {.Room = Room}
    End Function

    Public Shared Function CreateGuestStarting(Room As TaowaRoom, Difficulty As TaowaConnectionDifficulty) As TaowaAppState
        Return New TaowaAppState(TaowaAppStateKind.GuestStarting) With {.Room = Room, .Difficulty = Difficulty}
    End Function

    Public Shared Function CreateGuestOk(Room As TaowaRoom, ServerPort As Integer, Profiles As IEnumerable(Of TaowaProfile)) As TaowaAppState
        Return New TaowaAppState(TaowaAppStateKind.GuestOk) With {
            .Room = Room,
            .ServerPort = ServerPort,
            .Profiles = If(Profiles, New List(Of TaowaProfile)).Select(Function(p) p.Clone()).ToList()
        }
    End Function

    Public Shared Function CreateException(Kind As TaowaExceptionType) As TaowaAppState
        Return New TaowaAppState(TaowaAppStateKind.Exception) With {.ExceptionKind = Kind}
    End Function

    Public Function Clone() As TaowaAppState
        Dim Result As New TaowaAppState(Kind) With {
            .Room = Room,
            .Port = Port,
            .ServerPort = ServerPort,
            .Difficulty = Difficulty,
            .ExceptionKind = ExceptionKind,
            .Scanner = Scanner
        }
        If HostProfiles IsNot Nothing Then Result.HostProfiles = HostProfiles.Select(Function(p) p.Clone()).ToList()
        If Profiles IsNot Nothing Then Result.Profiles = Profiles.Select(Function(p) p.Clone()).ToList()
        Return Result
    End Function

    Public Function ToJson(Index As Integer, SharingIndex As Integer) As JObject
        Select Case Kind
            Case TaowaAppStateKind.Waiting
                Return New JObject(New JProperty("state", "waiting"), New JProperty("index", Index))
            Case TaowaAppStateKind.HostScanning
                Return New JObject(New JProperty("state", "host-scanning"), New JProperty("index", Index))
            Case TaowaAppStateKind.HostStarting
                Return New JObject(New JProperty("state", "host-starting"), New JProperty("index", Index), New JProperty("room", Room.Code))
            Case TaowaAppStateKind.HostOk
                Return New JObject(
                    New JProperty("state", "host-ok"),
                    New JProperty("index", Index),
                    New JProperty("room", Room.Code),
                    New JProperty("profile_index", SharingIndex),
                    New JProperty("profiles", New JArray(If(HostProfiles, New List(Of TaowaTimedProfile)).Select(Function(p) p.Profile.ToJson()))))
            Case TaowaAppStateKind.GuestConnecting
                Return New JObject(New JProperty("state", "guest-connecting"), New JProperty("index", Index), New JProperty("room", Room.Code))
            Case TaowaAppStateKind.GuestStarting
                Return New JObject(
                    New JProperty("state", "guest-starting"),
                    New JProperty("index", Index),
                    New JProperty("room", Room.Code),
                    New JProperty("difficulty", Difficulty.ToString().ToUpperInvariant()))
            Case TaowaAppStateKind.GuestOk
                Dim Url = If(ServerPort = 25565, "127.0.0.1", "127.0.0.1:" & ServerPort)
                Return New JObject(
                    New JProperty("state", "guest-ok"),
                    New JProperty("index", Index),
                    New JProperty("url", Url),
                    New JProperty("profile_index", SharingIndex),
                    New JProperty("profiles", New JArray(If(Profiles, New List(Of TaowaProfile)).Select(Function(p) p.ToJson()))))
            Case TaowaAppStateKind.Exception
                Return New JObject(
                    New JProperty("state", "exception"),
                    New JProperty("index", Index),
                    New JProperty("type", CInt(ExceptionKind)))
            Case Else
                Return New JObject(New JProperty("state", "waiting"), New JProperty("index", Index))
        End Select
    End Function
End Class

Public Class TaowaMinecraftScanner
    Implements IDisposable

    Private Const LanPort As Integer = 4445
    Private ReadOnly Gate As New Object
    Private ReadOnly ActivePorts As New Dictionary(Of Integer, Date)
    Private ReadOnly Cancellation As New Threading.CancellationTokenSource
    Private ReadOnly MotdFilter As Func(Of String, Boolean)

    Private Sub New(Filter As Func(Of String, Boolean))
        MotdFilter = If(Filter, Function(__) True)
        ThreadPool.QueueUserWorkItem(Sub() ScanIpv4())
        ThreadPool.QueueUserWorkItem(Sub() ScanIpv6())
    End Sub

    Public Shared Function Create(Optional Filter As Func(Of String, Boolean) = Nothing) As TaowaMinecraftScanner
        Return New TaowaMinecraftScanner(Filter)
    End Function

    Public Function GetPorts() As List(Of Integer)
        SyncLock Gate
            PruneLocked()
            Return ActivePorts.Keys.ToList()
        End SyncLock
    End Function

    Private Sub ScanIpv4()
        Try
            Using Sock As New Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
                Sock.ExclusiveAddressUse = False
                Sock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, True)
                Sock.Bind(New IPEndPoint(IPAddress.Any, LanPort))
                Sock.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, New MulticastOption(IPAddress.Parse("224.0.2.60"), IPAddress.Any))
                Sock.ReceiveTimeout = 500
                ReceiveLoop(Sock)
            End Using
        Catch ex As Exception
            LogScanner("IPv4 scanner stopped: " & ex.Message)
        End Try
    End Sub

    Private Sub ScanIpv6()
        Try
            Using Sock As New Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp)
                Sock.ExclusiveAddressUse = False
                Sock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, True)
                Sock.Bind(New IPEndPoint(IPAddress.IPv6Any, LanPort))
                Sock.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.AddMembership, New IPv6MulticastOption(IPAddress.Parse("FF75:230::60")))
                Sock.ReceiveTimeout = 500
                ReceiveLoop(Sock)
            End Using
        Catch ex As Exception
            LogScanner("IPv6 scanner stopped: " & ex.Message)
        End Try
    End Sub

    Private Sub ReceiveLoop(Sock As Socket)
        Dim Buffer(8191) As Byte
        Do While Not Cancellation.IsCancellationRequested
            Try
                Dim Remote As EndPoint = If(Sock.AddressFamily = AddressFamily.InterNetworkV6,
                    CType(New IPEndPoint(IPAddress.IPv6Any, 0), EndPoint),
                    CType(New IPEndPoint(IPAddress.Any, 0), EndPoint))
                Dim Length = Sock.ReceiveFrom(Buffer, Remote)
                Dim Port = ParseLanAnnouncement(Encoding.UTF8.GetString(Buffer, 0, Length))
                If Port.HasValue Then RememberPort(Port.Value)
            Catch ex As SocketException
                If ex.SocketErrorCode <> SocketError.TimedOut AndAlso ex.SocketErrorCode <> SocketError.WouldBlock Then Thread.Sleep(200)
            Catch ex As ObjectDisposedException
                Return
            Catch ex As Exception
                Thread.Sleep(200)
            End Try
        Loop
    End Sub

    Private Function ParseLanAnnouncement(Data As String) As Integer?
        Dim Motd = ReadTag(Data, "[MOTD]", "[/MOTD]")
        If String.IsNullOrEmpty(Motd) OrElse Not MotdFilter(Motd) Then Return Nothing

        Dim PortText = ReadTag(Data, "[AD]", "[/AD]")
        Dim Port As Integer
        If Integer.TryParse(PortText, Port) AndAlso Port > 0 AndAlso Port <= 65535 Then Return Port
        Return Nothing
    End Function

    Private Shared Function ReadTag(Data As String, BeginTag As String, EndTag As String) As String
        If Data Is Nothing Then Return Nothing
        Dim BeginIndex = Data.IndexOf(BeginTag, StringComparison.Ordinal)
        If BeginIndex < 0 Then Return Nothing
        BeginIndex += BeginTag.Length
        Dim EndIndex = Data.IndexOf(EndTag, BeginIndex, StringComparison.Ordinal)
        If EndIndex <= BeginIndex Then Return Nothing
        Return Data.Substring(BeginIndex, EndIndex - BeginIndex)
    End Function

    Private Sub RememberPort(Port As Integer)
        SyncLock Gate
            ActivePorts(Port) = Date.UtcNow
            PruneLocked()
        End SyncLock
    End Sub

    Private Sub PruneLocked()
        Dim Now = Date.UtcNow
        For Each Entry In ActivePorts.ToList()
            If (Now - Entry.Value).TotalMilliseconds >= 5000 Then ActivePorts.Remove(Entry.Key)
        Next
    End Sub

    Private Sub LogScanner(Message As String)
        Try
            Dim LogFolder = Path.Combine(If(AppDomain.CurrentDomain.BaseDirectory, "."), "PCL")
            DirectoryUtils.Create(LogFolder)
            File.AppendAllText(Path.Combine(LogFolder, "TaowaCore.log"), Date.Now.ToString("HH:mm:ss.fff") & " " & Message & vbCrLf, Encoding.UTF8)
        Catch
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Cancellation.Cancel()
        Cancellation.Dispose()
    End Sub
End Class
