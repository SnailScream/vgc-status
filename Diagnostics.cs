using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using Microsoft.Win32;

namespace VgcStatus
{
    /// <summary>
    /// Собирает всё, что известно Windows о службе: тип запуска, путь к файлу,
    /// зависимости, коды выхода и последние события журнала System.
    /// </summary>
    internal static class ServiceDiagnostics
    {
        private const int SC_MANAGER_CONNECT = 0x0001;
        private const int SERVICE_QUERY_STATUS = 0x0004;
        private const int SC_STATUS_PROCESS_INFO = 0;

        private const string VgtrayPath = @"C:\Program Files\Riot Vanguard\vgtray.exe";

        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_STATUS_PROCESS
        {
            public int dwServiceType;
            public int dwCurrentState;
            public int dwControlsAccepted;
            public int dwWin32ExitCode;
            public int dwServiceSpecificExitCode;
            public int dwCheckPoint;
            public int dwWaitHint;
            public int dwProcessId;
            public int dwServiceFlags;
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern IntPtr OpenSCManager(string machine, string database, int access);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern IntPtr OpenService(IntPtr scManager, string name, int access);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool QueryServiceStatusEx(IntPtr service, int level, IntPtr buffer, int size, out int needed);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CloseServiceHandle(IntPtr handle);

        public static string BuildReport(string serviceName, Exception failure)
        {
            StringBuilder report = new StringBuilder();
            List<string> causes = new List<string>();

            report.AppendLine(Loc.Of("Служба: ", "Service: ") + serviceName);
            report.AppendLine(Loc.Of("Отчёт создан: ", "Report created: ") +
                              DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"));
            report.AppendLine(new string('-', 66));

            RegistryKey serviceKey = null;
            try
            {
                RegistryView view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;
                using (RegistryKey localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                {
                    serviceKey = localMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + serviceName);
                    if (serviceKey == null)
                    {
                        report.AppendLine(Loc.Of("Установлена: НЕТ (нет раздела в реестре)",
                                                 "Installed: NO (no registry key)"));
                        causes.Add(Loc.Of(
                            "Служба не зарегистрирована в системе. Скорее всего Vanguard не установлен " +
                            "или установлен повреждённо — переустановите его.",
                            "The service is not registered in the system. Vanguard is probably not installed " +
                            "or the installation is damaged — reinstall it."));
                        AppendCauses(report, causes);
                        return report.ToString();
                    }

                    ServiceControllerStatus status;
                    try
                    {
                        using (ServiceController sc = new ServiceController(serviceName))
                        {
                            status = sc.Status;
                        }
                        report.AppendLine(Loc.Of("Состояние: ", "State: ") + DescribeStatus(status));
                    }
                    catch (InvalidOperationException)
                    {
                        report.AppendLine(Loc.Of("Состояние: неизвестно (служба не отвечает)",
                                                 "State: unknown (the service is not responding)"));
                    }

                    object startValue = serviceKey.GetValue("Start");
                    int startType = startValue is int ? (int)startValue : -1;
                    report.AppendLine(Loc.Of("Тип запуска: ", "Start type: ") + DescribeStartType(startType) +
                                      " (Start=" + (startType < 0 ? "?" : startType.ToString()) + ")");

                    object displayName = serviceKey.GetValue("DisplayName");
                    if (displayName != null)
                    {
                        report.AppendLine(Loc.Of("Отображаемое имя: ", "Display name: ") + displayName);
                    }

                    object account = serviceKey.GetValue("ObjectName");
                    report.AppendLine(Loc.Of("Учётная запись: ", "Account: ") +
                                      (account == null ? "LocalSystem" : account.ToString()));

                    string imagePath = serviceKey.GetValue("ImagePath") as string;
                    if (string.IsNullOrEmpty(imagePath))
                    {
                        report.AppendLine(Loc.Of("Файл службы: не задан (ImagePath пуст)",
                                                 "Service file: not set (ImagePath is empty)"));
                        causes.Add(Loc.Of(
                            "В реестре не указан файл службы (ImagePath). Служба повреждена — переустановите Vanguard.",
                            "ImagePath is missing in the registry. The service is damaged — reinstall Vanguard."));
                    }
                    else
                    {
                        string exePath = ExtractExecutablePath(imagePath);
                        bool exists = false;
                        try
                        {
                            exists = File.Exists(exePath);
                        }
                        catch
                        {
                        }

                        report.AppendLine(Loc.Of("Файл службы: ", "Service file: ") + imagePath);
                        if (exists)
                        {
                            report.AppendLine(Loc.Of("  → файл найден", "  -> file found"));
                        }
                        else
                        {
                            report.AppendLine(Loc.Of("  → ФАЙЛ НЕ НАЙДЕН: ", "  -> FILE NOT FOUND: ") + exePath);
                            causes.Add(string.Format(Loc.Of(
                                "Файл службы отсутствует: {0}. Переустановите Vanguard (или античит) на этом ПК.",
                                "The service file is missing: {0}. Reinstall Vanguard (or the anti-cheat) on this PC."),
                                exePath));
                        }
                    }

                    string[] dependencies = serviceKey.GetValue("DependOnService") as string[];
                    if (dependencies == null || dependencies.Length == 0)
                    {
                        report.AppendLine(Loc.Of("Зависимости: нет", "Dependencies: none"));
                    }
                    else
                    {
                        report.AppendLine(Loc.Of("Зависимости:", "Dependencies:"));
                        foreach (string dependency in dependencies)
                        {
                            string dependencyState;
                            bool dependencyOk;
                            try
                            {
                                using (ServiceController dep = new ServiceController(dependency))
                                {
                                    ServiceControllerStatus depStatus = dep.Status;
                                    dependencyState = DescribeStatus(depStatus);
                                    dependencyOk = depStatus == ServiceControllerStatus.Running;
                                }
                            }
                            catch (InvalidOperationException)
                            {
                                dependencyState = Loc.Of("НЕ НАЙДЕНА", "NOT FOUND");
                                dependencyOk = false;
                            }

                            report.AppendLine("  • " + dependency + " — " + dependencyState);
                            if (!dependencyOk)
                            {
                                causes.Add(string.Format(Loc.Of(
                                    "Зависимость «{0}» не работает. Запустите её и повторите попытку.",
                                    "Dependency \"{0}\" is not running. Start it and try again."),
                                    dependency));
                            }
                        }
                    }

                    int win32Exit;
                    int serviceExit;
                    int processId;
                    if (TryQueryExitCodes(serviceName, out win32Exit, out serviceExit, out processId))
                    {
                        report.AppendLine(Loc.Of("Код выхода Win32: ", "Win32 exit code: ") + win32Exit +
                                          " (" + DescribeWin32Error(win32Exit) + ")");
                        if (serviceExit != 0)
                        {
                            report.AppendLine(Loc.Of("Код выхода службы: ", "Service exit code: ") + serviceExit);
                        }
                        report.AppendLine("PID: " + (processId == 0
                            ? Loc.Of("нет (процесс не запущен)", "none (process is not running)")
                            : processId.ToString()));

                        if (win32Exit != 0 && win32Exit != 1077)
                        {
                            causes.Add(Loc.Of("Последняя попытка запуска завершилась кодом ",
                                              "The last start attempt ended with code ") +
                                       win32Exit + " — " + DescribeWin32Error(win32Exit) + ".");
                        }
                    }

                    if (startType == 4)
                    {
                        causes.Add(Loc.Of(
                            "Тип запуска «Отключена» — Windows запрещает старт. Включите вручную в консоли " +
                            "от администратора: sc config " + serviceName + " start= demand",
                            "Start type is Disabled — Windows blocks startup. Enable it manually in an " +
                            "administrator console: sc config " + serviceName + " start= demand"));
                    }
                }
            }
            catch (Exception ex)
            {
                report.AppendLine(Loc.Of("Не удалось прочитать реестр: ", "Failed to read the registry: ") + ex.Message);
            }
            finally
            {
                if (serviceKey != null)
                {
                    serviceKey.Close();
                }
            }

            if (failure != null)
            {
                report.AppendLine(new string('-', 66));
                report.AppendLine(Loc.Of("Ошибка при попытке запуска:", "Error while starting:"));
                AppendException(report, failure);
            }

            report.AppendLine(new string('-', 66));
            AppendVgtraySection(report, causes);

            report.AppendLine(new string('-', 66));
            AppendDriverSection(report, causes, "vgk");

            report.AppendLine(new string('-', 66));
            report.AppendLine(Loc.Of("События журнала System за последние 24 часа (vgc / vgk):",
                                     "System event log, last 24 hours (vgc / vgk):"));
            bool eventsFound = AppendRecentEvents(report, serviceName, "vgk");
            if (eventsFound)
            {
                causes.Add(Loc.Of(
                    "В журнале System есть свежие ошибки, связанные со службой — см. список выше.",
                    "There are recent errors in the System log related to the service — see the list above."));
            }

            AppendCauses(report, causes);
            return report.ToString();
        }

        private static void AppendVgtraySection(StringBuilder report, List<string> causes)
        {
            report.AppendLine(Loc.Of("Процесс vgtray (трей Vanguard):", "vgtray process (Vanguard tray):"));

            bool fileExists = false;
            try
            {
                fileExists = File.Exists(VgtrayPath);
            }
            catch
            {
            }
            report.AppendLine(Loc.Of("  Файл: ", "  File: ") + VgtrayPath +
                              (fileExists ? Loc.Of(" (найден)", " (found)") : Loc.Of(" (НЕ НАЙДЕН)", " (NOT FOUND)")));

            bool running = false;
            int processId = 0;
            Process[] processes = null;
            try
            {
                processes = Process.GetProcessesByName("vgtray");
                running = processes.Length > 0;
                if (running)
                {
                    processId = processes[0].Id;
                }
            }
            catch
            {
            }
            finally
            {
                if (processes != null)
                {
                    foreach (Process process in processes)
                    {
                        process.Dispose();
                    }
                }
            }

            report.AppendLine(Loc.Of("  Состояние: ", "  State: ") + (running
                ? Loc.Of("запущен (PID " + processId + ")", "running (PID " + processId + ")")
                : Loc.Of("не запущен", "not running")));

            if (!running && fileExists)
            {
                causes.Add(string.Format(Loc.Of(
                    "Трей vgtray не запущен. Нажмите «Запустить vgtray» в главном окне или запустите {0} вручную.",
                    "The vgtray tray is not running. Press \"Start vgtray\" in the main window or run {0} manually."),
                    VgtrayPath));
            }
        }

        private static void AppendDriverSection(StringBuilder report, List<string> causes, string driverName)
        {
            report.AppendLine(Loc.Of("Драйвер ", "Driver ") + driverName +
                              Loc.Of(" (ядро, Vanguard):", " (kernel, Vanguard):"));

            RegistryKey driverKey = null;
            try
            {
                RegistryView view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;
                using (RegistryKey localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                {
                    driverKey = localMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + driverName);
                    if (driverKey == null)
                    {
                        report.AppendLine(Loc.Of("  не установлен в системе", "  not installed on this system"));
                        causes.Add(Loc.Of(
                            "Драйвер " + driverName + " не установлен. Без него античит не работает — переустановите Vanguard.",
                            "The " + driverName + " driver is not installed. The anti-cheat cannot work without it — " +
                            "reinstall Vanguard."));
                        return;
                    }

                    bool running = false;
                    try
                    {
                        using (ServiceController driver = new ServiceController(driverName))
                        {
                            ServiceControllerStatus driverStatus = driver.Status;
                            running = driverStatus == ServiceControllerStatus.Running;
                            report.AppendLine(Loc.Of("  Состояние: ", "  State: ") + DescribeStatus(driverStatus) +
                                              (driverStatus == ServiceControllerStatus.Running
                                                  ? Loc.Of(" (драйвер загружен)", " (driver is loaded)")
                                                  : Loc.Of(" (драйвер не загружен)", " (driver is not loaded)")));
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        report.AppendLine(Loc.Of("  Состояние: неизвестно (нет доступа к драйверу)",
                                                 "  State: unknown (no access to the driver)"));
                    }

                    bool alreadyReported = false;
                    object driverStartValue = driverKey.GetValue("Start");
                    int driverStartType = driverStartValue is int ? (int)driverStartValue : -1;
                    report.AppendLine(Loc.Of("  Тип запуска: ", "  Start type: ") + DescribeStartType(driverStartType) +
                                      " (Start=" + (driverStartType < 0 ? "?" : driverStartType.ToString()) + ")");

                    string driverImage = driverKey.GetValue("ImagePath") as string;
                    if (!string.IsNullOrEmpty(driverImage))
                    {
                        string driverPath = ExtractDriverPath(driverImage);
                        bool driverFileExists = false;
                        try
                        {
                            driverFileExists = File.Exists(driverPath);
                        }
                        catch
                        {
                        }

                        report.AppendLine(Loc.Of("  Файл драйвера: ", "  Driver file: ") + driverPath);
                        report.AppendLine(driverFileExists
                            ? Loc.Of("    → файл найден", "    -> file found")
                            : Loc.Of("    → ФАЙЛ НЕ НАЙДЕН", "    -> FILE NOT FOUND"));
                        if (!driverFileExists)
                        {
                            causes.Add(string.Format(Loc.Of(
                                "Файл драйвера {0}.sys отсутствует: {1}. Переустановите Vanguard.",
                                "Driver file {0}.sys is missing: {1}. Reinstall Vanguard."),
                                driverName, driverPath));
                            alreadyReported = true;
                        }
                    }

                    bool vgtrayExists = false;
                    try
                    {
                        vgtrayExists = File.Exists(VgtrayPath);
                    }
                    catch
                    {
                    }
                    report.AppendLine("  vgtray.exe: " +
                                      (vgtrayExists ? Loc.Of("найден", "found") : Loc.Of("НЕ НАЙДЕН", "NOT FOUND")));

                    int driverWin32Exit;
                    int driverServiceExit;
                    int driverPid;
                    if (TryQueryExitCodes(driverName, out driverWin32Exit, out driverServiceExit, out driverPid) &&
                        driverWin32Exit != 0 && driverWin32Exit != 1077)
                    {
                        report.AppendLine(Loc.Of("  Код выхода Win32: ", "  Win32 exit code: ") +
                                          driverWin32Exit + " (" + DescribeWin32Error(driverWin32Exit) + ")");
                        causes.Add(Loc.Of("Драйвер ", "The driver ") + driverName +
                                   Loc.Of(" завершился с кодом ", " exited with code ") + driverWin32Exit +
                                   " — " + DescribeWin32Error(driverWin32Exit) + ".");
                        alreadyReported = true;
                    }

                    if (!running && !alreadyReported)
                    {
                        causes.Add(Loc.Of(
                            "Драйвер " + driverName + " не загружен, поэтому античит не работает. " +
                            (vgtrayExists
                                ? "Запустите vgtray: кнопка «Запустить vgtray» в главном окне или " +
                                  @"C:\Program Files\Riot Vanguard\vgtray.exe. Если не помогает — перезагрузите ПК."
                                : "Перезагрузите ПК; если не помогает — переустановите Vanguard."),
                            "The " + driverName + " driver is not loaded, so the anti-cheat is not working. " +
                            (vgtrayExists
                                ? "Start vgtray: the \"Start vgtray\" button in the main window or " +
                                  @"C:\Program Files\Riot Vanguard\vgtray.exe. If that does not help, reboot the PC."
                                : "Reboot the PC; if that does not help, reinstall Vanguard.")));
                    }
                }
            }
            catch (Exception ex)
            {
                report.AppendLine(Loc.Of("  не удалось прочитать данные драйвера: ",
                                         "  failed to read driver data: ") + ex.Message);
            }
            finally
            {
                if (driverKey != null)
                {
                    driverKey.Close();
                }
            }
        }

        private static void AppendCauses(StringBuilder report, List<string> causes)
        {
            report.AppendLine(new string('-', 66));
            report.AppendLine(Loc.Of("Вероятная причина:", "Likely cause:"));
            if (causes.Count == 0)
            {
                report.AppendLine(Loc.Of(
                    "  Явных проблем не найдено: служба установлена, файл на месте, тип запуска не «Отключена».",
                    "  No obvious problems found: the service is installed, the file exists and the start type is not Disabled."));
                report.AppendLine(Loc.Of(
                    "  Если запуск всё равно не удаётся, проверьте журнал событий Windows целиком",
                    "  If it still fails to start, check the full Windows event log"));
                report.AppendLine(Loc.Of(
                    "  («Просмотр событий» → Журнал Windows → Система) в момент запуска.",
                    "  (Event Viewer -> Windows Logs -> System) at the moment of startup."));
                return;
            }

            for (int i = 0; i < causes.Count; i++)
            {
                report.AppendLine("  " + (i + 1) + ") " + causes[i]);
            }
        }

        private static void AppendException(StringBuilder report, Exception exception)
        {
            Exception root = exception;
            int level = 0;
            while (root != null && level < 4)
            {
                report.AppendLine(new string(' ', level * 2) + root.GetType().Name + ": " + root.Message);

                Win32Exception win32 = root as Win32Exception;
                if (win32 != null && win32.NativeErrorCode != 0)
                {
                    report.AppendLine(new string(' ', level * 2 + 2) + Loc.Of("Код: ", "Code: ") +
                                      win32.NativeErrorCode + " — " + DescribeWin32Error(win32.NativeErrorCode));
                }

                root = root.InnerException;
                level++;
            }
        }

        private static bool AppendRecentEvents(StringBuilder report, string serviceName, string secondName)
        {
            bool found = false;
            EventLog log = null;
            try
            {
                log = new EventLog("System", ".");
                DateTime limit = DateTime.Now.AddHours(-24);
                int scanned = 0;
                int matches = 0;

                for (int i = log.Entries.Count - 1; i >= 0 && scanned < 4000 && matches < 6; i--, scanned++)
                {
                    EventLogEntry entry;
                    try
                    {
                        entry = log.Entries[i];
                    }
                    catch
                    {
                        continue;
                    }

                    if (entry.TimeGenerated < limit)
                    {
                        break;
                    }

                    if (entry.EntryType != EventLogEntryType.Error && entry.EntryType != EventLogEntryType.Warning)
                    {
                        continue;
                    }

                    string message;
                    try
                    {
                        message = entry.Message ?? "";
                    }
                    catch
                    {
                        message = "";
                    }

                    if (message.IndexOf(serviceName, StringComparison.OrdinalIgnoreCase) < 0 &&
                        message.IndexOf(secondName, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    found = true;
                    matches++;
                    report.AppendLine("  [" + entry.TimeGenerated.ToString("dd.MM HH:mm:ss") + "] " +
                                      entry.Source + " (ID " + (entry.InstanceId & 0xFFFF) + ", " +
                                      DescribeEventType(entry.EntryType) + ")");
                    report.AppendLine("      " + Shorten(message, 300));
                }

                if (!found)
                {
                    report.AppendLine(Loc.Of("  записей об этой службе не найдено",
                                             "  no entries about this service were found"));
                }
            }
            catch (Exception ex)
            {
                report.AppendLine(Loc.Of("  журнал недоступен: ", "  the log is unavailable: ") + ex.Message);
                report.AppendLine(Loc.Of("  (чтение журнала требует прав администратора)",
                                         "  (reading the log requires administrator rights)"));
            }
            finally
            {
                if (log != null)
                {
                    log.Close();
                }
            }

            return found;
        }

        private static bool TryQueryExitCodes(string serviceName, out int win32Exit, out int serviceExit, out int processId)
        {
            win32Exit = 0;
            serviceExit = 0;
            processId = 0;

            IntPtr manager = IntPtr.Zero;
            IntPtr service = IntPtr.Zero;
            IntPtr buffer = IntPtr.Zero;

            try
            {
                manager = OpenSCManager(null, null, SC_MANAGER_CONNECT);
                if (manager == IntPtr.Zero)
                {
                    return false;
                }

                service = OpenService(manager, serviceName, SERVICE_QUERY_STATUS);
                if (service == IntPtr.Zero)
                {
                    return false;
                }

                int size = Marshal.SizeOf(typeof(SERVICE_STATUS_PROCESS));
                buffer = Marshal.AllocHGlobal(size);
                int needed;
                if (!QueryServiceStatusEx(service, SC_STATUS_PROCESS_INFO, buffer, size, out needed))
                {
                    return false;
                }

                SERVICE_STATUS_PROCESS status =
                    (SERVICE_STATUS_PROCESS)Marshal.PtrToStructure(buffer, typeof(SERVICE_STATUS_PROCESS));

                win32Exit = status.dwWin32ExitCode;
                serviceExit = status.dwServiceSpecificExitCode;
                processId = status.dwProcessId;
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                if (service != IntPtr.Zero) CloseServiceHandle(service);
                if (manager != IntPtr.Zero) CloseServiceHandle(manager);
            }
        }

        private static string ExtractExecutablePath(string imagePath)
        {
            string path = Environment.ExpandEnvironmentVariables(imagePath.Trim());

            if (path.StartsWith("\"", StringComparison.Ordinal))
            {
                int closing = path.IndexOf('"', 1);
                if (closing > 1)
                {
                    return path.Substring(1, closing - 1);
                }
            }

            int exe = path.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe > 0)
            {
                return path.Substring(0, exe + 4);
            }

            int space = path.IndexOf(' ');
            return space > 0 ? path.Substring(0, space) : path;
        }

        private static string ExtractDriverPath(string imagePath)
        {
            string path = Environment.ExpandEnvironmentVariables(imagePath.Trim());

            if (path.StartsWith(@"\??\", StringComparison.Ordinal))
            {
                path = path.Substring(4);
            }

            if (path.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            {
                string windows = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
                path = Path.Combine(windows, path.Substring(12));
            }

            try
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }
            catch
            {
            }

            int sys = path.IndexOf(".sys", StringComparison.OrdinalIgnoreCase);
            return sys > 0 ? path.Substring(0, sys + 4) : path;
        }

        private static string DescribeStatus(ServiceControllerStatus status)
        {
            switch (status)
            {
                case ServiceControllerStatus.Running: return Loc.Of("Работает", "Running");
                case ServiceControllerStatus.Stopped: return Loc.Of("Остановлена", "Stopped");
                case ServiceControllerStatus.StartPending: return Loc.Of("Запускается", "Starting");
                case ServiceControllerStatus.StopPending: return Loc.Of("Останавливается", "Stopping");
                case ServiceControllerStatus.Paused: return Loc.Of("Пауза", "Paused");
                case ServiceControllerStatus.PausePending: return Loc.Of("Приостановка", "Pausing");
                case ServiceControllerStatus.ContinuePending: return Loc.Of("Продолжение", "Resuming");
                default: return status.ToString();
            }
        }

        private static string DescribeStartType(int startType)
        {
            switch (startType)
            {
                case 0: return Loc.Of("Загрузочный драйвер", "Boot driver");
                case 1: return Loc.Of("Системный драйвер", "System driver");
                case 2: return Loc.Of("Автоматически", "Automatic");
                case 3: return Loc.Of("Вручную", "Manual");
                case 4: return Loc.Of("Отключена", "Disabled");
                default: return Loc.Of("неизвестно", "unknown");
            }
        }

        private static string DescribeEventType(EventLogEntryType type)
        {
            switch (type)
            {
                case EventLogEntryType.Error: return Loc.Of("ошибка", "error");
                case EventLogEntryType.Warning: return Loc.Of("предупреждение", "warning");
                case EventLogEntryType.Information: return Loc.Of("информация", "information");
                default: return type.ToString();
            }
        }

        private static string DescribeWin32Error(int code)
        {
            switch (code)
            {
                case 0: return Loc.Of("успешно", "success");
                case 2: return Loc.Of("файл не найден", "file not found");
                case 3: return Loc.Of("путь не найден", "path not found");
                case 5: return Loc.Of("отказано в доступе", "access denied");
                case 1053: return Loc.Of("служба не ответила вовремя", "the service did not respond in time");
                case 1058: return Loc.Of("служба отключена (тип запуска «Отключена»)", "the service is disabled");
                case 1060: return Loc.Of("служба не найдена", "the service was not found");
                case 1066: return Loc.Of("код ошибки, зависящий от службы", "service-specific error code");
                case 1077: return Loc.Of("попыток запуска ещё не было", "no start attempts yet");
                case 1079: return Loc.Of("служба отмечена как интерактивная", "the service is marked as interactive");
                case 126: return Loc.Of("не найден модуль (библиотека службы)", "module not found (service library)");
                case 127: return Loc.Of("не найдена процедура в библиотеке службы",
                                        "procedure not found in the service library");
                case 577: return Loc.Of("не удалось проверить цифровую подпись файла",
                                        "failed to verify the digital signature of the file");
                default:
                    try
                    {
                        return new Win32Exception(code).Message;
                    }
                    catch
                    {
                        return Loc.Of("неизвестная ошибка", "unknown error");
                    }
            }
        }

        private static string Shorten(string text, int max)
        {
            text = text.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            while (text.IndexOf("  ", StringComparison.Ordinal) >= 0)
            {
                text = text.Replace("  ", " ");
            }
            text = text.Trim();
            return text.Length > max ? text.Substring(0, max - 1) + "…" : text;
        }
    }
}
