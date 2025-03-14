﻿Imports NAudio.CoreAudioApi
Imports NAudio.CoreAudioApi.Interfaces

Public Module ModMusic

#Region "播放列表"

    ''' <summary>
    ''' 接下来要播放的音乐文件路径。未初始化时为 Nothing。
    ''' </summary>
    Public MusicWaitingList As List(Of String) = Nothing
    ''' <summary>
    ''' 全部音乐文件路径。未初始化时为 Nothing。
    ''' </summary>
    Public MusicAllList As List(Of String) = Nothing
    ''' <summary>
    ''' 初始化音乐播放列表。
    ''' </summary>
    ''' <param name="ForceReload">强制全部重新载入列表。</param>
    ''' <param name="PreventFirst">在重载列表时避免让某项成为第一项。</param>
    Private Sub MusicListInit(ForceReload As Boolean, Optional PreventFirst As String = Nothing)
        If ForceReload Then MusicAllList = Nothing
        Try
            '初始化全部可用音乐列表
            If MusicAllList Is Nothing Then
                MusicAllList = New List(Of String)
                Directory.CreateDirectory(Path & "PCL\Musics\")
                For Each File In EnumerateFiles(Path & "PCL\Musics\")
                    '文件夹可能会被加入 .ini 文件夹配置文件、一些乱七八糟的 .jpg 文件啥的
                    Dim Ext As String = File.Extension.ToLower
                    If {".ini", ".jpg", ".txt", ".cfg", ".lrc", ".db", ".png"}.Contains(Ext) Then Continue For
                    MusicAllList.Add(File.FullName)
                Next
            End If
            '打乱顺序播放
            MusicWaitingList = If(Setup.Get("UiMusicRandom"), Shuffle(New List(Of String)(MusicAllList)), New List(Of String)(MusicAllList))
            If PreventFirst IsNot Nothing AndAlso MusicWaitingList.FirstOrDefault = PreventFirst Then
                '若需要避免成为第一项的为第一项，则将它放在最后
                MusicWaitingList.RemoveAt(0)
                MusicWaitingList.Add(PreventFirst)
            End If
        Catch ex As Exception
            Log(ex, "初始化音乐列表失败", LogLevel.Feedback)
        End Try
    End Sub
    ''' <summary>
    ''' 获取下一首播放的音乐路径并将其从列表中移除。
    ''' 如果没有，可能会返回 Nothing。
    ''' </summary>
    Private Function DequeueNextMusicAddress() As String
        '初始化，确保存在音乐
        If MusicAllList Is Nothing OrElse Not MusicAllList.Any() OrElse Not MusicWaitingList.Any() Then MusicListInit(False)
        '出列下一个音乐，如果出列结束则生成新列表
        If MusicWaitingList.Any() Then
            DequeueNextMusicAddress = MusicWaitingList(0)
            MusicWaitingList.RemoveAt(0)
        Else
            DequeueNextMusicAddress = Nothing
        End If
        If Not MusicWaitingList.Any() Then MusicListInit(False, DequeueNextMusicAddress)
    End Function

#End Region

#Region "UI 控制"

    ''' <summary>
    ''' 刷新背景音乐按钮 UI 与设置页 UI。
    ''' </summary>
    Private Sub MusicRefreshUI()
        RunInUi(
            Sub()
                Try

                    If Not MusicAllList.Any() Then
                        '无背景音乐
                        FrmMain.BtnExtraMusic.Show = False
                    Else
                        '有背景音乐
                        FrmMain.BtnExtraMusic.Show = True
                        Dim ToolTipText As String
                        If MusicState = MusicStates.Pause Then
                            FrmMain.BtnExtraMusic.Logo = Logo.IconPlay
                            FrmMain.BtnExtraMusic.LogoScale = 0.8
                            ToolTipText = "已暂停：" & GetFileNameWithoutExtentionFromPath(MusicCurrent)
                            If MusicAllList.Count > 1 Then
                                ToolTipText += vbCrLf & "左键恢复播放，右键播放下一曲。"
                            Else
                                ToolTipText += vbCrLf & "左键恢复播放，右键重新从头播放。"
                            End If
                        Else
                            FrmMain.BtnExtraMusic.Logo = Logo.IconMusic
                            FrmMain.BtnExtraMusic.LogoScale = 1
                            ToolTipText = "正在播放：" & GetFileNameWithoutExtentionFromPath(MusicCurrent)
                            If MusicAllList.Count > 1 Then
                                ToolTipText += vbCrLf & "左键暂停，右键播放下一曲。"
                            Else
                                ToolTipText += vbCrLf & "左键暂停，右键重新从头播放。"
                            End If
                        End If
                        FrmMain.BtnExtraMusic.ToolTip = ToolTipText
                        ToolTipService.SetVerticalOffset(FrmMain.BtnExtraMusic, If(ToolTipText.Contains(vbLf), 10, 16))
                    End If
                    If FrmSetupUI IsNot Nothing Then FrmSetupUI.MusicRefreshUI()

                Catch ex As Exception
                    Log(ex, "刷新背景音乐 UI 失败", LogLevel.Feedback)
                End Try
            End Sub)
    End Sub

    ''' <summary>
    ''' 让音乐在暂停、播放间切换，并显示提示文本。
    ''' </summary>
    Public Sub MusicControlPause()
        If MusicNAudio Is Nothing Then
            Hint("音乐播放尚未开始！", HintType.Critical)
        Else
            Select Case MusicState
                Case MusicStates.Pause
                    MusicResume()
                Case MusicStates.Play
                    MusicPause()
                Case Else
                    Log("[Music] 音乐目前为停止状态，已强制尝试开始播放", LogLevel.Debug)
                    MusicRefreshPlay(False)
            End Select
        End If
    End Sub

    ''' <summary>
    ''' 播放下一曲，并显示提示文本。
    ''' </summary>
    Public Sub MusicControlNext()
        If MusicAllList.Count = 1 Then
            MusicStartPlay(MusicCurrent)
            Hint("重新播放：" & GetFileNameFromPath(MusicCurrent), HintType.Finish)
        Else
            Dim Address As String = DequeueNextMusicAddress()
            If Address Is Nothing Then
                Hint("没有可以播放的音乐！", HintType.Critical)
            Else
                MusicStartPlay(Address)
                Hint("正在播放：" & GetFileNameFromPath(Address), HintType.Finish)
            End If
        End If
        MusicRefreshUI()
    End Sub

#End Region

#Region "主状态控制"

    ''' <summary>
    ''' 获取当前的音乐播放状态。
    ''' </summary>
    Public ReadOnly Property MusicState As MusicStates
        Get
            If MusicNAudio Is Nothing Then Return MusicStates.Stop
            Select Case MusicNAudio.PlaybackState
                Case 0 'NAudio.Wave.PlaybackState.Stopped
                    Return MusicStates.Stop
                Case 2 'NAudio.Wave.PlaybackState.Paused
                    Return MusicStates.Pause
                Case Else
                    Return MusicStates.Play
            End Select
        End Get
    End Property
    Public Enum MusicStates
        [Stop]
        Play
        Pause
    End Enum

    '重载与开始

    ''' <summary>
    ''' 重载播放列表并尝试开始播放背景音乐。
    ''' </summary>
    ''' <param name="ShowHint">是否显示刷新提示。</param>
    Public Sub MusicRefreshPlay(ShowHint As Boolean, Optional IsFirstLoad As Boolean = False)
        Try

            MusicListInit(True)
            If Not MusicAllList.Any() Then
                If MusicNAudio Is Nothing Then
                    If ShowHint Then Hint("未检测到可用的背景音乐！", HintType.Critical)
                Else
                    MusicNAudio = Nothing
                    If ShowHint Then Hint("背景音乐已清除！", HintType.Finish)
                End If
            Else
                Dim Address As String = DequeueNextMusicAddress()
                If Address Is Nothing Then
                    If ShowHint Then Hint("没有可以播放的音乐！", HintType.Critical)
                Else
                    Try
                        MusicStartPlay(Address, IsFirstLoad)
                        If ShowHint Then Hint("背景音乐已刷新：" & GetFileNameFromPath(Address), HintType.Finish, False)
                    Catch
                    End Try
                End If
            End If
            MusicRefreshUI()

        Catch ex As Exception
            Log(ex, "刷新背景音乐播放失败", LogLevel.Feedback)
        End Try
    End Sub
    ''' <summary>
    ''' 开始播放音乐。
    ''' </summary>
    Private Sub MusicStartPlay(Address As String, Optional IsFirstLoad As Boolean = False)
        If Address Is Nothing Then Return
        Log("[Music] 播放开始：" & Address)
        MusicCurrent = Address
        RunInNewThread(Sub() MusicLoop(IsFirstLoad), "Music", ThreadPriority.BelowNormal)
    End Sub

    '播放与暂停

    ''' <summary>
    ''' 暂停音乐播放，返回是否成功切换了状态。
    ''' </summary>
    Public Function MusicPause() As Boolean
        If MusicState = MusicStates.Play Then
            RunInThread(
                Sub()
                    Log("[Music] 已暂停播放")
                    MusicNAudio?.Pause()
                    MusicRefreshUI()
                End Sub)
            Return True
        Else
            Log($"[Music] 无需暂停播放，当前状态为 {MusicState}")
            Return False
        End If
    End Function
    ''' <summary>
    ''' 继续音乐播放，返回是否成功切换了状态。
    ''' </summary>
    Public Function MusicResume() As Boolean
        If MusicState = MusicStates.Play OrElse Not MusicAllList.Any() Then
            Log($"[Music] 无需继续播放，当前状态为 {MusicState}")
            Return False
        Else
            RunInThread(
                Sub()
                    Log("[Music] 已恢复播放")
                    MusicNAudio?.Play()
                    MusicRefreshUI()
                End Sub)
            Return True
        End If
    End Function

#End Region

    ''' <summary>
    ''' 当前正在播放的 NAudio.Wave.WaveOut。
    ''' </summary>
    Public MusicNAudio = Nothing
    ''' <summary>
    ''' 当前播放的音乐地址。
    ''' </summary>
    Private MusicCurrent As String = ""

    Dim deviceHandler As New AudioDeviceChangeHandler

    ''' <summary>
    ''' 在 MusicUuid 不变的前提下，持续播放某地址的音乐，且在播放结束后随机播放下一曲。
    ''' </summary>
    Private Sub MusicLoop(Optional IsFirstLoad As Boolean = False)
        Dim CurrentWave As NAudio.Wave.WaveOut = Nothing
        Dim Reader As NAudio.Wave.WaveStream = Nothing
        Try
            '开始播放
            CurrentWave = New NAudio.Wave.WaveOut()
            MusicNAudio = CurrentWave
            Reader = New NAudio.Wave.AudioFileReader(MusicCurrent)
            CurrentWave.Init(Reader)
            CurrentWave.Play()
            '第一次打开的暂停
            If IsFirstLoad AndAlso Not Setup.Get("UiMusicAuto") Then CurrentWave.Pause()
            MusicRefreshUI()
            '停止条件：播放完毕或变化
            Dim PreviousVolume = 0
            While CurrentWave.Equals(MusicNAudio) AndAlso Not CurrentWave.PlaybackState = NAudio.Wave.PlaybackState.Stopped
                If Setup.Get("UiMusicVolume") <> PreviousVolume Then
                    '更新音量
                    PreviousVolume = Setup.Get("UiMusicVolume")
                    CurrentWave.Volume = PreviousVolume / 1000
                End If
                '更新进度条
                Dim Percent = Reader.CurrentTime.TotalMilliseconds / Reader.TotalTime.TotalMilliseconds
                RunInUi(Sub() FrmMain.BtnExtraMusic.Progress = Percent)
                Thread.Sleep(100)
            End While
            '当前音乐已播放结束，继续下一曲
            If CurrentWave.PlaybackState = NAudio.Wave.PlaybackState.Stopped AndAlso MusicAllList.Any Then MusicStartPlay(DequeueNextMusicAddress)
        Catch ex As Exception
            Log(ex, "播放音乐出现内部错误（" & MusicCurrent & "）", LogLevel.Developer)
            If TypeOf ex Is NAudio.MmException AndAlso (ex.Message.Contains("NoDriver") OrElse ex.Message.Contains("BadDeviceId")) Then
                Hint("由于音频设备变更，音乐播放功能在重启 PCL 后才能恢复！", HintType.Critical)
                Thread.Sleep(1000000000)
            End If
            If ex.Message.Contains("Got a frame at sample rate") OrElse ex.Message.Contains("does not support changes to") Then
                Hint("播放音乐失败（" & GetFileNameFromPath(MusicCurrent) & "）：PCL 不支持播放音频属性在中途发生变化的音乐", HintType.Critical)
            ElseIf Not (MusicCurrent.EndsWithF(".wav", True) OrElse MusicCurrent.EndsWithF(".mp3", True) OrElse MusicCurrent.EndsWithF(".flac", True)) OrElse
                    ex.Message.Contains("0xC00D36C4") Then '#5096：不支持给定的 URL 的字节流类型。 (异常来自 HRESULT:0xC00D36C4)
                Hint("播放音乐失败（" & GetFileNameFromPath(MusicCurrent) & "）：PCL 可能不支持此音乐格式，请将格式转换为 .wav、.mp3 或 .flac 后再试", HintType.Critical)
            Else
                Log(ex, "播放音乐失败（" & GetFileNameFromPath(MusicCurrent) & "）", LogLevel.Hint)
            End If
            '将播放错误的音乐从列表中移除
            MusicAllList.Remove(MusicCurrent)
            MusicWaitingList.Remove(MusicCurrent)
            MusicRefreshUI()
            '等待 2 秒后继续播放
            Thread.Sleep(2000)
            If TypeOf ex Is FileNotFoundException Then
                MusicRefreshPlay(True, IsFirstLoad)
            Else
                MusicStartPlay(DequeueNextMusicAddress(), IsFirstLoad)
            End If
        Finally
            If CurrentWave IsNot Nothing Then CurrentWave.Dispose()
            If Reader IsNot Nothing Then Reader.Dispose()
            MusicRefreshUI()
        End Try
    End Sub

    Public Class AudioDeviceChangeHandler
        Implements Interfaces.IMMNotificationClient
        Private deviceWatcher As MMDeviceEnumerator
        Private callback As IMMNotificationClient

        Public Sub New()
            ' 初始化音频设备枚举器
            deviceWatcher = New MMDeviceEnumerator()
            ' 创建设备通知回调对象
            callback = New DeviceNotificationClient(Me)
            ' 注册回调以接收音频设备更改通知
            deviceWatcher.RegisterEndpointNotificationCallback(callback)
        End Sub

        ' 实现IMMNotificationClient接口的方法以处理设备更改事件
        Public Sub OnDefaultDeviceChanged(dataFlow As DataFlow, role As Role, defaultDeviceId As String) Implements IMMNotificationClient.OnDefaultDeviceChanged
            ' 处理默认音频设备更改事件
            If MusicNAudio Is Nothing Then Exit Sub
            ' 切换到系统默认输出设备
            Log($"音频默认输出设备切换到 {defaultDeviceId}")
            Dim target As NAudio.Wave.WaveOut = MusicNAudio
            target.Stop()
            target.DeviceNumber = 0
            target.Resume()
        End Sub

        Public Sub OnDeviceAdded(deviceId As String) Implements IMMNotificationClient.OnDeviceAdded
            ' 处理音频设备添加事件
            If MusicNAudio Is Nothing Then Exit Sub
            CType(MusicNAudio, NAudio.Wave.WaveOut).DeviceNumber = 0
        End Sub

        Public Sub OnDeviceRemoved(deviceId As String) Implements IMMNotificationClient.OnDeviceRemoved
            ' 处理音频设备移除事件
            If MusicNAudio Is Nothing Then Exit Sub
            CType(MusicNAudio, NAudio.Wave.WaveOut).DeviceNumber = 0
        End Sub

        Public Sub OnDeviceStateChanged(deviceId As String, newState As DeviceState) Implements IMMNotificationClient.OnDeviceStateChanged
            ' 处理音频设备状态更改事件
        End Sub

        Public Sub OnPropertyValueChanged(deviceId As String, key As PropertyKey) Implements IMMNotificationClient.OnPropertyValueChanged
            ' 处理音频设备属性值更改事件
        End Sub
    End Class

    ' 实现IMMNotificationClient接口的类
    Friend Class DeviceNotificationClient
        Implements IMMNotificationClient

        Private container As AudioDeviceChangeHandler

        Public Sub New(container As AudioDeviceChangeHandler)
            Me.container = container
        End Sub

        Public Sub OnDefaultDeviceChanged(dataFlow As DataFlow, role As Role, defaultDeviceId As String) Implements IMMNotificationClient.OnDefaultDeviceChanged
            container.OnDefaultDeviceChanged(dataFlow, role, defaultDeviceId)
        End Sub

        Public Sub OnDeviceAdded(deviceId As String) Implements IMMNotificationClient.OnDeviceAdded
            container.OnDeviceAdded(deviceId)
        End Sub

        Public Sub OnDeviceRemoved(deviceId As String) Implements IMMNotificationClient.OnDeviceRemoved
            container.OnDeviceRemoved(deviceId)
        End Sub

        Public Sub OnDeviceStateChanged(deviceId As String, newState As DeviceState) Implements IMMNotificationClient.OnDeviceStateChanged
            container.OnDeviceStateChanged(deviceId, newState)
        End Sub

        Public Sub OnPropertyValueChanged(deviceId As String, key As PropertyKey) Implements IMMNotificationClient.OnPropertyValueChanged
            container.OnPropertyValueChanged(deviceId, key)
        End Sub
    End Class

End Module
