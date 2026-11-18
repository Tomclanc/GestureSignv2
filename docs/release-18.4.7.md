# GestureSign V2 18.4.7

- 合入另一台电脑上的 AI 修复：否决按钮等待引擎确认状态，切换后旧刷新结果不能覆盖新状态；连接失败时显示状态未确认，避免误报开启或关闭。
- 双指普通绘制手势的模板分数至少 85、领先其他手势至少 10 分，且两根手指都具备明确对应转向时，可以保留被低意图评分误拦截的动作。模板分数不是 AI 意图评分，也不是实测准确率。
- 该放行不适用于智能关闭、近期滚动或连续滚动场景，也不适用于缺少模板转向、一指或三/四指手势；这些场景保留原保护逻辑。
- 学习组件训练或硬件初始化期间也可关闭 AI 否决，并保存关闭状态，后台学习开关保持独立。
- 修复管理员运行时配置导入、导出的文件选择器兼容性；保留配置格式。
- 修正被禁用计划任务的自启动状态显示，加入只读自启动检查脚本。
- 为新增 AI 状态补齐 90 个语言目录，保留 18.4.6 的音量、连续手势和多语言修复。

AI 组件也更新到 18.4.7。已有 18.4.2 组件仍兼容；如需训练中关闭否决的修复，请在 AI 页面卸载旧组件后下载对应的 CPU 或硬件加速版，已有样本和模型保留。主程序包不预装 AI 或 Kando。

## English

- Keep the AI veto button synchronized with acknowledged engine state and ignore refresh results started before a switch change. Connection failures leave the state unconfirmed.
- Preserve ordinary two-finger curved gestures with strong, unambiguous template evidence despite a low intent-model score. Require a template score of at least 85, a 10-point lead, and matching sustained turns on both fingers.
- Retain Smart Close, recent/continuous scrolling, missing-turn and other-contact-count safeguards. Template similarity and intent scores are distinct; neither is measured recognition accuracy.
- Allow veto-off during training/hardware initialization and persist the choice independently of background learning.
- Use administrator-compatible configuration file pickers and report disabled startup tasks correctly.
- Include AI component 18.4.7 and translated AI status labels for all 90 locales.

Existing 18.4.2 learning components remain compatible. To receive the busy-engine veto-off fix, uninstall/re-download the optional learning component from the AI page; local samples and models are retained.

Downloads: x64 / ARM64 MSI and portable ZIP; four optional CPU/hardware AI component archives. The Store bundle is supplied separately for Partner Center submission and does not indicate certification approval.

Translations are machine-assisted with selected revisions. Automated resource and placeholder checks do not replace native-language proofreading or touchpad testing on every device.
