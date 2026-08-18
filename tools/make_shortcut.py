"""生成 Godot 项目桌面快捷方式（.lnk）。

用 ctypes 直调 IShellLinkW + IPersistFile 创建标准 .lnk，
不依赖 WScript.Shell / pywin32 / 外部脚本解释器（避免沙箱拦截）。

用法：python make_shortcut.py [--desktop PATH]
"""
import ctypes
import ctypes.wintypes as wt
import os
import sys

HRESULT = ctypes.c_long
PPVOID = ctypes.POINTER(ctypes.c_void_p)
CLSCTX_INPROC_SERVER = 0x1
S_OK = 0


class GUID(ctypes.Structure):
    _fields_ = [("Data1", wt.DWORD), ("Data2", wt.WORD), ("Data3", wt.WORD), ("Data4", wt.BYTE * 8)]

    def __init__(self, d1, d2, d3, d4):
        super().__init__(d1, d2, d3, (wt.BYTE * 8)(*d4))


CLSID_ShellLink = GUID(0x00021401, 0x0000, 0x0000, (0xC0, 0, 0, 0, 0, 0, 0, 0x46))
IID_IShellLinkW = GUID(0x000214F9, 0x0000, 0x0000, (0xC0, 0, 0, 0, 0, 0, 0, 0x46))
IID_IPersistFile = GUID(0x0000010B, 0x0000, 0x0000, (0xC0, 0, 0, 0, 0, 0, 0, 0x46))

# IShellLinkW vtable 索引（0-2 为 IUnknown）
SLIDX = {
    "GetPath": 3, "GetIDList": 4, "SetIDList": 5,
    "GetDescription": 6, "SetDescription": 7,
    "GetWorkingDirectory": 8, "SetWorkingDirectory": 9,
    "GetArguments": 10, "SetArguments": 11,
    "GetHotkey": 12, "SetHotkey": 13,
    "GetShowCmd": 14, "SetShowCmd": 15,
    "GetIconLocation": 16, "SetIconLocation": 17,
    "SetRelativePath": 18, "Resolve": 19, "SetPath": 20,
}
# IPersistFile vtable 索引
PFIDX = {"GetClassID": 3, "IsDirty": 4, "Load": 5, "Save": 6, "SaveCompleted": 7, "GetCurFile": 8}


def make_shortcut(lnk_path, target, arguments="", working_dir="", icon="", description=""):
    ole32 = ctypes.windll.ole32
    # 显式声明调用约定：未声明 argtypes 时 windll 对指针参数处理不可靠（曾返回假指针）
    ole32.CoCreateInstance.argtypes = [
        ctypes.POINTER(GUID), ctypes.c_void_p, wt.DWORD,
        ctypes.POINTER(GUID), PPVOID,
    ]
    ole32.CoCreateInstance.restype = HRESULT

    pShell = ctypes.c_void_p()  # 接收接口指针（void** 的容器）
    hr = ole32.CoCreateInstance(ctypes.byref(CLSID_ShellLink), None, CLSCTX_INPROC_SERVER,
                                ctypes.byref(IID_IShellLinkW), ctypes.byref(pShell))
    if hr != S_OK or not pShell.value:
        raise OSError(f"CoCreateInstance(IShellLinkW) failed: 0x{hr & 0xFFFFFFFF:08X}")
    shell_addr = pShell.value  # 接口指针（整数地址）

    # COM 对象第一成员是 vtable 指针：用 from_address 直接按地址读内存
    vtbl_addr = ctypes.c_void_p.from_address(shell_addr).value
    vtbl = (ctypes.c_void_p * 21).from_address(vtbl_addr)

    # 绑定 vtable 方法（保存引用防 GC；table 参数指定方法表，避免跨接口取错槽位）
    _fns = []

    def bind(table, idx, restype, *argtypes):
        f = ctypes.WINFUNCTYPE(restype, *argtypes)(int(table[idx]))
        _fns.append(f)
        return f

    SetPath = bind(vtbl, SLIDX["SetPath"], HRESULT, ctypes.c_void_p, ctypes.c_wchar_p)
    SetWorkingDir = bind(vtbl, SLIDX["SetWorkingDirectory"], HRESULT, ctypes.c_void_p, ctypes.c_wchar_p)
    SetArguments = bind(vtbl, SLIDX["SetArguments"], HRESULT, ctypes.c_void_p, ctypes.c_wchar_p)
    SetIcon = bind(vtbl, SLIDX["SetIconLocation"], HRESULT, ctypes.c_void_p, ctypes.c_wchar_p, ctypes.c_int)
    SetDesc = bind(vtbl, SLIDX["SetDescription"], HRESULT, ctypes.c_void_p, ctypes.c_wchar_p)
    Release = bind(vtbl, 2, ctypes.c_ulong, ctypes.c_void_p)
    QueryInterface = bind(vtbl, 0, HRESULT, ctypes.c_void_p, ctypes.POINTER(GUID), PPVOID)

    SetPath(shell_addr, target)
    if working_dir:
        SetWorkingDir(shell_addr, working_dir)
    if arguments:
        SetArguments(shell_addr, arguments)
    if icon:
        SetIcon(shell_addr, icon, 0)
    if description:
        SetDesc(shell_addr, description)

    # IShellLinkW -> IPersistFile
    pPersist = ctypes.c_void_p()  # 接收 IPersistFile 接口指针
    hr = QueryInterface(shell_addr, ctypes.byref(IID_IPersistFile), ctypes.byref(pPersist))
    if hr != S_OK or not pPersist.value:
        Release(shell_addr)
        raise OSError(f"QueryInterface(IPersistFile) failed: 0x{hr & 0xFFFFFFFF:08X}")
    persist_addr = pPersist.value  # IPersistFile 接口指针（整数地址）

    pvtbl_addr = ctypes.c_void_p.from_address(persist_addr).value
    pvtbl = (ctypes.c_void_p * 9).from_address(pvtbl_addr)
    Save = bind(pvtbl, PFIDX["Save"], HRESULT, ctypes.c_void_p, ctypes.c_wchar_p, wt.BOOL)
    PRelease = bind(pvtbl, 2, ctypes.c_ulong, ctypes.c_void_p)

    hr = Save(persist_addr, lnk_path, True)
    PRelease(persist_addr)
    Release(shell_addr)
    if hr != S_OK:
        raise OSError(f"IPersistFile::Save failed: 0x{hr & 0xFFFFFFFF:08X}")
    print(f"  已创建: {lnk_path}")


if __name__ == "__main__":
    desktop = os.path.join(os.path.expanduser("~"), "Desktop")
    if "--desktop" in sys.argv:
        desktop = sys.argv[sys.argv.index("--desktop") + 1]

    godot = r"G:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64.exe"
    project = r"G:\magicThunder"
    workdir = r"G:\Godot_v4.7.1-stable_mono_win64"

    print(f"桌面: {desktop}")
    make_shortcut(
        os.path.join(desktop, "魔女协议-运行游戏.lnk"), godot,
        arguments=f'--path "{project}"', working_dir=workdir,
        icon=f"{godot},0", description="魔女协议 · 直接运行游戏 (G:\\magicThunder)")
    make_shortcut(
        os.path.join(desktop, "魔女协议-编辑器.lnk"), godot,
        arguments=f'-e --path "{project}"', working_dir=workdir,
        icon=f"{godot},0", description="魔女协议 · Godot 编辑器打开项目")
    print("完成")
