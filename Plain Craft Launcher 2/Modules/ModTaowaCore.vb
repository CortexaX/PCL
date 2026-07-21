Imports System.Numerics
Imports System.Net.Sockets
Imports System.Security.Cryptography

Public Module Pcl2TaowaCore

    Private Const TerracottaVersion As String = "0.4.2"
    Private Const EasyTierVersion As String = "v2.5.0-terracotta.2"
    Public Const TaowaMotd As String = "§6§l双击进入陶瓦联机大厅（请保持陶瓦运行）"
    Public Const ScaffoldingDefaultPort As Integer = 13448
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

    Public Function SetGuestOk(Room As TaowaRoom, ServerPort As Integer, Profiles As IEnumerable(Of TaowaProfile), Capture As TaowaStateCapture,
                               Optional FakeServer As TaowaMinecraftFakeServer = Nothing) As TaowaStateCapture
        SyncLock StateLock
            If Not CanCaptureLocked(Capture) Then Return Nothing
            Return SetStateLocked(TaowaAppState.CreateGuestOk(Room, ServerPort, Profiles, FakeServer))
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
            IncreaseSharedLocked()
        End SyncLock
    End Sub

    Public Function CanCapture(Capture As TaowaStateCapture) As Boolean
        SyncLock StateLock
            Return CanCaptureLocked(Capture)
        End SyncLock
    End Function

    Private Function SetStateLocked(State As TaowaAppState) As TaowaStateCapture
        DisposeStateResourcesLocked(CurrentState, State)
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

    Private Sub IncreaseSharedLocked()
        CurrentStateIndex += 1
        CurrentSharingIndex += 1
        Log("TaowaCore shared -> " & CurrentState.Kind.ToString())
    End Sub

    Private Sub DisposeStateResourcesLocked(OldState As TaowaAppState, NewState As TaowaAppState)
        If OldState Is Nothing Then Return

        If OldState.Scanner IsNot Nothing AndAlso (NewState Is Nothing OrElse Not Object.ReferenceEquals(OldState.Scanner, NewState.Scanner)) Then
            Try
                OldState.Scanner.Dispose()
            Catch
            End Try
        End If

        If OldState.FakeServer IsNot Nothing AndAlso (NewState Is Nothing OrElse Not Object.ReferenceEquals(OldState.FakeServer, NewState.FakeServer)) Then
            Try
                OldState.FakeServer.Dispose()
            Catch
            End Try
        End If
    End Sub

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

    Public Function CreateScaffoldingResponse(Status As Byte, Body As Byte()) As Byte()
        If Body Is Nothing Then Body = New Byte() {}

        Dim Result As New List(Of Byte)(5 + Body.Length)
        Result.Add(Status)
        AddUInt32BigEndian(Result, CUInt(Body.Length))
        Result.AddRange(Body)
        Return Result.ToArray()
    End Function

    Public Function CreateDefaultScaffoldingHandlers() As List(Of TaowaScaffoldingHandlerEntry)
        Dim Entries As New List(Of TaowaScaffoldingHandlerEntry)
        Entries.Add(New TaowaScaffoldingHandlerEntry("c", "ping",
            Function(Request As Byte()) As TaowaPacketResponse
                Return TaowaPacketResponse.Ok(Request)
            End Function))
        Entries.Add(New TaowaScaffoldingHandlerEntry("c", "protocols",
            Function(Request As Byte()) As TaowaPacketResponse
                Dim Names = Entries.Select(Function(Entry) Entry.NamespaceName & ":" & Entry.PathName).ToArray()
                Return TaowaPacketResponse.Ok(Encoding.UTF8.GetBytes(String.Join(New String(ChrW(0), 1), Names)))
            End Function))
        Entries.Add(New TaowaScaffoldingHandlerEntry("c", "server_port", AddressOf HandleScaffoldingServerPort))
        Entries.Add(New TaowaScaffoldingHandlerEntry("c", "player_ping", AddressOf HandleScaffoldingPlayerPing))
        Entries.Add(New TaowaScaffoldingHandlerEntry("c", "player_profiles_list", AddressOf HandleScaffoldingPlayerProfilesList))
        Return Entries
    End Function

    Private Sub AddUInt32BigEndian(Target As List(Of Byte), Value As UInteger)
        Target.Add(CByte((Value >> 24) And &HFFUI))
        Target.Add(CByte((Value >> 16) And &HFFUI))
        Target.Add(CByte((Value >> 8) And &HFFUI))
        Target.Add(CByte(Value And &HFFUI))
    End Sub

    Private Function HandleScaffoldingServerPort(Request As Byte()) As TaowaPacketResponse
        SyncLock StateLock
            If CurrentState.Kind <> TaowaAppStateKind.HostOk OrElse CurrentState.Port <= 0 OrElse CurrentState.Port > 65535 Then
                Return TaowaPacketResponse.Fail(32)
            End If
            Return TaowaPacketResponse.Ok(New Byte() {
                CByte((CurrentState.Port >> 8) And &HFF),
                CByte(CurrentState.Port And &HFF)})
        End SyncLock
    End Function

    Private Function HandleScaffoldingPlayerPing(Request As Byte()) As TaowaPacketResponse
        Dim Root = JObject.Parse(Encoding.UTF8.GetString(If(Request, New Byte() {})))
        Dim Name = ReadRequiredString(Root, "name")
        Dim MachineId = ReadRequiredString(Root, "machine_id")
        Dim Vendor = ReadRequiredString(Root, "vendor")

        SyncLock StateLock
            If CurrentState.Kind <> TaowaAppStateKind.HostOk Then Throw New InvalidOperationException("IllegalStateException: Expecting HostOk.")
            If CurrentState.HostProfiles Is Nothing Then CurrentState.HostProfiles = New List(Of TaowaTimedProfile)

            Dim Profiles = CurrentState.HostProfiles
            Dim ExistingIndex = -1
            For i = 0 To Profiles.Count - 1
                If String.Equals(Profiles(i).Profile.MachineId, MachineId, StringComparison.Ordinal) Then
                    ExistingIndex = i
                    Exit For
                End If
            Next

            If ExistingIndex = 0 Then
                Throw New InvalidOperationException("IllegalStateException: Cannot modify host, machine_id may conflict.")
            ElseIf ExistingIndex > 0 Then
                Profiles(ExistingIndex).UpdatedAt = Date.UtcNow
                If Not String.Equals(Profiles(ExistingIndex).Profile.Name, Name, StringComparison.Ordinal) Then
                    Profiles(ExistingIndex).Profile.Name = Name
                    IncreaseSharedLocked()
                End If
            Else
                Profiles.Add(New TaowaTimedProfile(New TaowaProfile(MachineId, Name, Vendor, TaowaProfileKind.GUEST)))
                IncreaseSharedLocked()
            End If
        End SyncLock

        Return TaowaPacketResponse.Ok()
    End Function

    Private Function HandleScaffoldingPlayerProfilesList(Request As Byte()) As TaowaPacketResponse
        SyncLock StateLock
            If CurrentState.Kind <> TaowaAppStateKind.HostOk Then Throw New InvalidOperationException("IllegalStateException: Expecting HostOk.")
            If CurrentState.HostProfiles Is Nothing Then CurrentState.HostProfiles = New List(Of TaowaTimedProfile)
            If PruneHostProfilesLocked() Then IncreaseSharedLocked()

            Dim Profiles As New JArray
            For Each Entry In CurrentState.HostProfiles
                Profiles.Add(Entry.Profile.ToJson())
            Next
            Return TaowaPacketResponse.Ok(Encoding.UTF8.GetBytes(Profiles.ToString(Newtonsoft.Json.Formatting.None)))
        End SyncLock
    End Function

    Private Function PruneHostProfilesLocked() As Boolean
        If CurrentState.HostProfiles Is Nothing Then Return False

        Dim Changed = False
        Dim Now = Date.UtcNow
        For i = CurrentState.HostProfiles.Count - 1 To 1 Step -1
            If (Now - CurrentState.HostProfiles(i).UpdatedAt).TotalSeconds >= 10 Then
                CurrentState.HostProfiles.RemoveAt(i)
                Changed = True
            End If
        Next
        Return Changed
    End Function

    Private Function ReadRequiredString(Root As JObject, Name As String) As String
        Dim Token = Root.SelectToken(Name)
        If Token Is Nothing OrElse Token.Type <> JTokenType.String Then Throw New InvalidDataException("Missing JSON string field: " & Name)
        Return Token.ToString()
    End Function

#End Region

#Region "Minecraft"

    Public Function CheckMinecraftConnection(Port As Integer) As Boolean
        Dim Started = Date.UtcNow
        Try
            Using Client As New TcpClient(AddressFamily.InterNetwork)
                Client.ReceiveTimeout = 64000
                Client.SendTimeout = 64000

                Dim Result As IAsyncResult = Nothing
                Try
                    Result = Client.BeginConnect(IPAddress.Loopback, Port, Nothing, Nothing)
                    If Not Result.AsyncWaitHandle.WaitOne(64000) Then Throw New TimeoutException("Minecraft connect timeout")
                    Client.EndConnect(Result)
                Finally
                    If Result IsNot Nothing Then
                        Try
                            Result.AsyncWaitHandle.Close()
                        Catch
                        End Try
                    End If
                End Try

                Dim Stream = Client.GetStream()
                Stream.WriteByte(&HFE)
                Stream.Flush()
                Return Stream.ReadByte() = &HFF
            End Using
        Catch
        End Try

        Dim Remaining = 5000 - CInt((Date.UtcNow - Started).TotalMilliseconds)
        If Remaining > 0 Then Thread.Sleep(Remaining)
        Return False
    End Function

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

    Public Shared Function Ok(Optional Data As Byte() = Nothing) As TaowaPacketResponse
        Return New TaowaPacketResponse(0, If(Data, New Byte() {}))
    End Function

    Public Shared Function Fail(Status As Byte, Optional Data As Byte() = Nothing) As TaowaPacketResponse
        Return New TaowaPacketResponse(Status, If(Data, New Byte() {}))
    End Function

    Public ReadOnly Property IsOk As Boolean
        Get
            Return Status = 0
        End Get
    End Property
End Class

Public Delegate Function TaowaScaffoldingHandler(Request As Byte()) As TaowaPacketResponse

Public Class TaowaScaffoldingHandlerEntry
    Public ReadOnly Property NamespaceName As String
    Public ReadOnly Property PathName As String
    Public ReadOnly Property Handler As TaowaScaffoldingHandler

    Public Sub New(NamespaceName As String, PathName As String, Handler As TaowaScaffoldingHandler)
        If String.IsNullOrEmpty(NamespaceName) Then Throw New ArgumentException("Namespace is empty")
        If String.IsNullOrEmpty(PathName) Then Throw New ArgumentException("Path is empty")
        If Handler Is Nothing Then Throw New ArgumentNullException(NameOf(Handler))
        Me.NamespaceName = NamespaceName
        Me.PathName = PathName
        Me.Handler = Handler
    End Sub

    Public ReadOnly Property Key As String
        Get
            Return NamespaceName & ":" & PathName
        End Get
    End Property
End Class

Public Class TaowaScaffoldingRequest
    Public ReadOnly Property NamespaceName As String
    Public ReadOnly Property PathName As String
    Public ReadOnly Property Body As Byte()

    Public Sub New(NamespaceName As String, PathName As String, Body As Byte())
        Me.NamespaceName = NamespaceName
        Me.PathName = PathName
        Me.Body = If(Body, New Byte() {})
    End Sub
End Class

Public Class TaowaScaffoldingClientSession
    Implements IDisposable

    Private Const TimeoutMilliseconds As Integer = 64000
    Private ReadOnly Client As TcpClient
    Private ReadOnly StreamGate As New Object
    Private Alive As Boolean = True

    Private Sub New(Client As TcpClient)
        Me.Client = Client
        Me.Client.ReceiveTimeout = TimeoutMilliseconds
        Me.Client.SendTimeout = TimeoutMilliseconds
    End Sub

    Public Shared Function Open(Address As IPAddress, Port As Integer) As TaowaScaffoldingClientSession
        If Address Is Nothing Then Throw New ArgumentNullException(NameOf(Address))
        Dim Client As New TcpClient(Address.AddressFamily)
        Dim Result As IAsyncResult = Nothing
        Try
            Result = Client.BeginConnect(Address, Port, Nothing, Nothing)
            If Not Result.AsyncWaitHandle.WaitOne(TimeoutMilliseconds) Then
                Throw New TimeoutException("Scaffolding connect timeout")
            End If
            Client.EndConnect(Result)
            Return New TaowaScaffoldingClientSession(Client)
        Catch
            Try
                Client.Close()
            Catch
            End Try
            Throw
        Finally
            If Result IsNot Nothing Then
                Try
                    Result.AsyncWaitHandle.Close()
                Catch
                End Try
            End If
        End Try
    End Function

    Public Shared Function Open(Address As String, Port As Integer) As TaowaScaffoldingClientSession
        Return Open(IPAddress.Parse(Address), Port)
    End Function

    Public ReadOnly Property IsAlive As Boolean
        Get
            Return Alive AndAlso Client IsNot Nothing AndAlso Client.Connected
        End Get
    End Property

    Public Function SendSync(NamespaceName As String, PathName As String, Optional Body As Byte() = Nothing) As TaowaPacketResponse
        Dim Response = SendRawSync(NamespaceName, PathName, Body)
        If Response Is Nothing OrElse Not Response.IsOk Then Return Nothing
        Return Response
    End Function

    Public Function SendRawSync(NamespaceName As String, PathName As String, Optional Body As Byte() = Nothing) As TaowaPacketResponse
        If Not IsAlive Then Return Nothing

        Try
            SyncLock StreamGate
                Dim Request = Pcl2TaowaCore.CreateScaffoldingRequest(NamespaceName, PathName, Body)
                Dim Stream = Client.GetStream()
                Stream.Write(Request, 0, Request.Length)
                Stream.Flush()

                Dim Header = ReadExact(Stream, 5)
                Dim BodyLength = ReadUInt32BigEndian(Header, 1)
                Dim ResponseBody = ReadExact(Stream, BodyLength)
                Return New TaowaPacketResponse(Header(0), ResponseBody)
            End SyncLock
        Catch
            Alive = False
            Return Nothing
        End Try
    End Function

    Private Shared Function ReadExact(Stream As NetworkStream, Length As Integer) As Byte()
        If Length <= 0 Then Return New Byte() {}
        Dim Buffer(Length - 1) As Byte
        Dim Offset = 0
        Do While Offset < Length
            Dim Read = Stream.Read(Buffer, Offset, Length - Offset)
            If Read <= 0 Then Throw New EndOfStreamException()
            Offset += Read
        Loop
        Return Buffer
    End Function

    Private Shared Function ReadUInt32BigEndian(Buffer As Byte(), Offset As Integer) As Integer
        Dim Value = (CLng(Buffer(Offset)) << 24) Or (CLng(Buffer(Offset + 1)) << 16) Or (CLng(Buffer(Offset + 2)) << 8) Or CLng(Buffer(Offset + 3))
        If Value > Integer.MaxValue Then Throw New InvalidDataException("Scaffolding body is too large")
        Return CInt(Value)
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Alive = False
        Try
            Client.Close()
        Catch
        End Try
    End Sub
End Class

Public Class TaowaScaffoldingServer
    Implements IDisposable

    Private Const TimeoutMilliseconds As Integer = 64000
    Private ReadOnly Handlers As New Dictionary(Of String, TaowaScaffoldingHandler)(StringComparer.Ordinal)
    Private Listener As TcpListener = Nothing
    Private Cancelled As Boolean = False

    Private Sub New(Handlers As IEnumerable(Of TaowaScaffoldingHandlerEntry))
        If Handlers IsNot Nothing Then
            For Each Entry In Handlers
                Me.Handlers(Entry.Key) = Entry.Handler
            Next
        End If
    End Sub

    Public ReadOnly Property Port As Integer
        Get
            If Listener Is Nothing Then Return 0
            Return DirectCast(Listener.LocalEndpoint, IPEndPoint).Port
        End Get
    End Property

    Public Shared Function StartDefault(Handlers As IEnumerable(Of TaowaScaffoldingHandlerEntry)) As TaowaScaffoldingServer
        Try
            Return Start(Handlers, Pcl2TaowaCore.ScaffoldingDefaultPort)
        Catch
            Return Start(Handlers, 0)
        End Try
    End Function

    Public Shared Function Start(Handlers As IEnumerable(Of TaowaScaffoldingHandlerEntry), Port As Integer) As TaowaScaffoldingServer
        Dim Server As New TaowaScaffoldingServer(Handlers)
        Server.Listener = New TcpListener(IPAddress.Any, Port)
        Server.Listener.Start(128)
        ThreadPool.QueueUserWorkItem(Sub(__) Server.AcceptLoop())
        Return Server
    End Function

    Private Sub AcceptLoop()
        Do While Not Cancelled
            Try
                Dim Client = Listener.AcceptTcpClient()
                Client.ReceiveTimeout = TimeoutMilliseconds
                Client.SendTimeout = TimeoutMilliseconds
                ThreadPool.QueueUserWorkItem(Sub(__) HandleClient(Client))
            Catch ex As SocketException
                If Not Cancelled Then Thread.Sleep(200)
            Catch ex As ObjectDisposedException
                Return
            Catch ex As Exception
                If Not Cancelled Then Thread.Sleep(200)
            End Try
        Loop
    End Sub

    Private Sub HandleClient(Client As TcpClient)
        Using OwnedClient = Client
            Dim Stream = OwnedClient.GetStream()
            Do While Not Cancelled AndAlso OwnedClient.Connected
                Try
                    Dim Request = ReadRequest(Stream)
                    Dim Response = Dispatch(Request)
                    Dim Data = Pcl2TaowaCore.CreateScaffoldingResponse(Response.Status, Response.Data)
                    Stream.Write(Data, 0, Data.Length)
                    Stream.Flush()
                Catch
                    Return
                End Try
            Loop
        End Using
    End Sub

    Private Function Dispatch(Request As TaowaScaffoldingRequest) As TaowaPacketResponse
        Dim Handler As TaowaScaffoldingHandler = Nothing
        If Not Handlers.TryGetValue(Request.NamespaceName & ":" & Request.PathName, Handler) Then
            Return TaowaPacketResponse.Fail(255, Encoding.UTF8.GetBytes("Requested protocol hasn't been implemented."))
        End If

        Try
            Dim Response = Handler(Request.Body)
            Return If(Response, TaowaPacketResponse.Ok())
        Catch ex As Exception
            Return TaowaPacketResponse.Fail(255, Encoding.UTF8.GetBytes(ex.ToString()))
        End Try
    End Function

    Private Shared Function ReadRequest(Stream As NetworkStream) As TaowaScaffoldingRequest
        Dim KindSize = ReadExact(Stream, 1)(0)
        Dim Kind = Encoding.UTF8.GetString(ReadExact(Stream, KindSize))
        Dim Kinds = Kind.Split(":"c)
        If Kinds.Length <> 2 Then Throw New InvalidDataException("Invalid request kind.")

        Dim Size = ReadExact(Stream, 4)
        Dim BodyLength = ReadUInt32BigEndian(Size, 0)
        Return New TaowaScaffoldingRequest(Kinds(0), Kinds(1), ReadExact(Stream, BodyLength))
    End Function

    Private Shared Function ReadExact(Stream As NetworkStream, Length As Integer) As Byte()
        If Length <= 0 Then Return New Byte() {}
        Dim Buffer(Length - 1) As Byte
        Dim Offset = 0
        Do While Offset < Length
            Dim Read = Stream.Read(Buffer, Offset, Length - Offset)
            If Read <= 0 Then Throw New EndOfStreamException()
            Offset += Read
        Loop
        Return Buffer
    End Function

    Private Shared Function ReadUInt32BigEndian(Buffer As Byte(), Offset As Integer) As Integer
        Dim Value = (CLng(Buffer(Offset)) << 24) Or (CLng(Buffer(Offset + 1)) << 16) Or (CLng(Buffer(Offset + 2)) << 8) Or CLng(Buffer(Offset + 3))
        If Value > Integer.MaxValue Then Throw New InvalidDataException("Scaffolding body is too large")
        Return CInt(Value)
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Cancelled = True
        Try
            If Listener IsNot Nothing Then Listener.Stop()
        Catch
        End Try
    End Sub
End Class

Public Class TaowaAppState
    Public Property Kind As TaowaAppStateKind
    Public Property Room As TaowaRoom
    Public Property Port As Integer
    Public Property ServerPort As Integer
    Public Property Difficulty As TaowaConnectionDifficulty = TaowaConnectionDifficulty.Unknown
    Public Property ExceptionKind As TaowaExceptionType
    Public Property Scanner As TaowaMinecraftScanner
    Public Property FakeServer As TaowaMinecraftFakeServer
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

    Public Shared Function CreateGuestOk(Room As TaowaRoom, ServerPort As Integer, Profiles As IEnumerable(Of TaowaProfile),
                                         Optional FakeServer As TaowaMinecraftFakeServer = Nothing) As TaowaAppState
        Return New TaowaAppState(TaowaAppStateKind.GuestOk) With {
            .Room = Room,
            .ServerPort = ServerPort,
            .FakeServer = FakeServer,
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
            .Scanner = Scanner,
            .FakeServer = FakeServer
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

Public Class TaowaMinecraftFakeServer
    Implements IDisposable

    Private Const LanPort As Integer = 4445
    Private ReadOnly Cancellation As New Threading.CancellationTokenSource
    Private ReadOnly Motd As String
    Public ReadOnly Property Port As Integer

    Private Sub New(Port As Integer, Motd As String)
        Me.Port = Port
        Me.Motd = If(Motd, Pcl2TaowaCore.TaowaMotd)
        ThreadPool.QueueUserWorkItem(Sub(__) BroadcastLoop())
    End Sub

    Public Shared Function Create(Port As Integer, Optional Motd As String = Nothing) As TaowaMinecraftFakeServer
        Return New TaowaMinecraftFakeServer(Port, Motd)
    End Function

    Private Sub BroadcastLoop()
        Dim Targets As New List(Of Tuple(Of UdpClient, IPEndPoint))
        Try
            AddIpv4Target(Targets)
            AddIpv6Target(Targets)

            Dim Data = Encoding.UTF8.GetBytes("[MOTD]" & Motd & "[/MOTD][AD]" & Port & "[/AD]")
            Do While Not Cancellation.IsCancellationRequested
                For Each Target In Targets
                    Try
                        Target.Item1.Send(Data, Data.Length, Target.Item2)
                    Catch
                    End Try
                Next

                If Cancellation.Token.WaitHandle.WaitOne(1500) Then Return
            Loop
        Finally
            For Each Target In Targets
                Try
                    Target.Item1.Close()
                Catch
                End Try
            Next
        End Try
    End Sub

    Private Sub AddIpv4Target(Targets As List(Of Tuple(Of UdpClient, IPEndPoint)))
        Try
            Dim Client As New UdpClient(AddressFamily.InterNetwork)
            Client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, True)
            Client.Client.Bind(New IPEndPoint(IPAddress.Any, 0))
            Client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 4)
            Client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, True)
            Targets.Add(Tuple.Create(Client, New IPEndPoint(IPAddress.Parse("224.0.2.60"), LanPort)))
        Catch
        End Try
    End Sub

    Private Sub AddIpv6Target(Targets As List(Of Tuple(Of UdpClient, IPEndPoint)))
        Try
            Dim Client As New UdpClient(AddressFamily.InterNetworkV6)
            Client.Client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastLoopback, True)
            Client.Client.Bind(New IPEndPoint(IPAddress.IPv6Any, 0))
            Targets.Add(Tuple.Create(Client, New IPEndPoint(IPAddress.Parse("FF75:230::60"), LanPort)))
        Catch
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Cancellation.Cancel()
        Cancellation.Dispose()
    End Sub
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
