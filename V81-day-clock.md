# V81 局内昼夜流速 1.0.3

TimeOfDay.Awake 沿用原有的倍率入口，将 V81 原始昼夜流速乘以 0.4，原整合包在相同入口乘以 0.6。没有改变 Unity 时间缩放或逻辑更新频率。Experimentation 真实房主旧记录中 122.31 秒推进 102.69 世界时间单位，相当于约 0.84/秒。隔离 V81 新 DLL 的 globalTimeSpeedMultiplier 为 0.56（1.4 × 0.4），落地连续两个四秒窗口分别推进约 2.24 单位，Time.timeScale 保持 1。
