# G87 V2 / Logitech Lightspeed 适配证据与缺口

本轮仅完成官方来源静态核对，未启用新的设备查询或宣称电量适配完成。当前电脑未枚举到目标设备。

## G87 V2

官方网页驱动main-DCRwBIvm.js SHA256：`405102805e2adeb93073ee2186f6d0193ca1317a6b4657d636e4e611bd25a446`。

- 明确产品键：MCHOSE G87 V2 (1K)，VID14391/PID8218，即3837:201A；MCHOSE 2.4G Wireless-G87_V2，VID14391/PID8219，即3837:201B。
- 过滤页65282，即FF02；仅页值不能证明集合长度、usage或实际报告能力。
- diffRID[G87_V2]指定有线report9、无线report19，不能套用G7鼠标的report0/65路径。
- getBattery有线模板为06 87 00 00 01 00 02 00后512个零；驱动去掉模板首字节，由实际report9调用sendFeatureReport，再receiveFeatureReport。transformHandler默认切去2字节，getBatteryParser的有线分支跳6字节后读电量及两个4位状态。
- 无线模板13 4A 01后零填充，追加字节累加校验；sendReport之后用commandParams、current和total匹配分包，合并每包slice(4,-1)后再解析电量。不能从任意原始响应固定位置直接取百分比。
- 状态位解析存在，但当前尚未独立确认所有状态枚举与充电语义，不猜充电字段。

需要新版设备报告中的实际产品串、连接模式、usage、集合长度和report能力；还需确认请求关联、分包长度、校验和与响应头，之后才能加入精确实例读取及回归测试。

## Logitech Lightspeed

Lightspeed是接收器族名称，不是唯一外设型号。当前初代GPW专用路径只接受C539/C088并要求实时型号身份，之后才能使用初代GPW电压模型。不得把其他Lightspeed鼠标或键盘套成GPW。

需要用户确认具体销售型号，或提供新版设备报告的VID/PID、集合和身份响应。未取得这些资料前不扩大主动查询范围，不把名字或接收器编号当作已连接外设身份。
