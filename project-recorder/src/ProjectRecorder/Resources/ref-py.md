# Python 中文速查（详细版）

> 面向 Python 3.8+；覆盖语法、内置类型、常用标准库、进阶特性与常见坑。

## 目录
- [基础语法](#基础语法)
- [数据类型与转换](#数据类型与转换)
- [字符串](#字符串)
- [字符串格式化](#字符串格式化)
- [列表 / 元组 / 字典 / 集合](#列表--元组--字典--集合)
- [切片与解包](#切片与解包)
- [控制流](#控制流)
- [推导式](#推导式)
- [函数](#函数)
- [函数进阶](#函数进阶)
- [类与对象](#类与对象)
- [面向对象进阶](#面向对象进阶)
- [异常处理](#异常处理)
- [上下文管理器](#上下文管理器)
- [文件与路径](#文件与路径)
- [常用内置函数](#常用内置函数)
- [常用标准库](#常用标准库)
- [正则表达式](#正则表达式)
- [JSON 与日期时间](#json-与日期时间)
- [类型注解](#类型注解)
- [异步 asyncio](#异步-asyncio)
- [虚拟环境与工具](#虚拟环境与工具)
- [实用技巧与坑](#实用技巧与坑)

## 基础语法

```python
# 注释
x = 1
name = "Tom"
print(f"你好 {name}")
a, b = 1, 2            # 多重赋值
a = b = 0
PI = 3.14              # 约定：全大写常量
```

运算符：`+ - * / // % **`、比较 `== != < <= > >=`、逻辑 `and or not`、成员 `in`、身份 `is`。

```python
7 / 2      # 3.5
7 // 2     # 3
7 % 2      # 1
2 ** 10    # 1024
divmod(7, 2)   # (3, 1)
```

缩进（4 空格）表示代码块；大小写敏感；`input()` 返回字符串。

## 数据类型与转换

```python
int("10"); float("1.5"); str(123); bool(0)
list("abc"); tuple([1,2]); set([1,1,2]); dict(a=1)
type(x); isinstance(x, int)
```

内置常量：`None` `True` `False` `Ellipsis (...)`。
可变：`list dict set`；不可变：`int float str tuple frozenset`。

## 字符串

```python
s = "Hello, 世界"
s[0]; s[-1]; s[0:5]; s[::2]; s[::-1]     # 索引/切片/反转
len(s)
s.upper(); s.lower(); s.title(); s.capitalize()
s.strip(); s.lstrip(); s.rstrip()
s.split(","); ",".join(["a","b"])
s.replace("l","L"); s.replace("l","L",1)
s.startswith("He"); s.endswith("界")
"e" in s
s.find("l"); s.rfind("l"); s.index("l")
s.count("l")
s.zfill(5); s.center(10,"*"); s.ljust(6); s.rjust(6)
s.isdigit(); s.isalpha(); s.isspace(); s.isalnum()
"abc".encode("utf-8"); b"abc".decode("utf-8")
r"C:\new\test"          # 原始字符串
```

## 字符串格式化

```python
n, pi = 3, 3.14159
f"值={n}, 平方={n**2}"        # f-string（推荐，3.6+）
f"{pi:.2f}"; f"{n:03d}"; f"{1234567:,}"
f"{name!r}"                   # repr
f"{'左':<6}|{'中':^6}|{'右':>6}"

"{} {}".format(1, 2)
"{0}-{1}-{0}".format("a","b")
"%s=%d, %.2f" % ("x", 3, 3.14159)   # 旧式
```

## 列表 / 元组 / 字典 / 集合

```python
# 列表 list（有序、可变）
lst = [1, 2, 3]
lst.append(4); lst.insert(0, 0); lst.extend([5,6])
lst.pop(); lst.pop(0); lst.remove(2); del lst[0]
lst.sort(reverse=True); lst.reverse()
sorted(lst); sorted(lst, key=abs)
lst.index(3); lst.count(1)
lst.copy(); lst.clear()
sum(lst); min(lst); max(lst); any(lst); all(lst)

# 元组 tuple（不可变、可作字典键）
t = (1, 2, 3)
(1,)        # 单元素必须有逗号

# 字典 dict
d = {"a": 1, "b": 2}
d["c"] = 3
d.get("x", 0); d.setdefault("e", 5)
d.keys(); d.values(); d.items()
del d["a"]; d.pop("b", None); d.popitem()
"a" in d
d.update({"d": 4})
{k: v for k, v in d.items()}

# 集合 set（去重、无序）
s = {1, 2, 3}
s.add(4); s.discard(1); s.remove(3)
s & {2,3}; s | {4}; s - {1}; s ^ {3,9}
frozenset([1,2])     # 不可变集合
```

## 切片与解包

```python
a = list(range(10))
a[2:5]; a[:3]; a[3:]; a[::2]; a[::-1]
a[1:5:2]
first, *rest = [1,2,3,4]
head, *mid, tail = [1,2,3,4,5]
x, y = y, x                     # 交换
(a, b), c = (1, 2), 3           # 嵌套解包
```

## 控制流

```python
if a > b: ...
elif a == b: ...
else: ...

for i in range(5): ...
for i in range(2, 10, 2): ...
for idx, val in enumerate(lst, start=1): ...
for x, y in zip(xs, ys): ...
for k, v in d.items(): ...
else:               # 循环未 break 时执行
    ...

while cond: ...
while True:
    break / continue

label = "大" if x > 5 else "小"

# 海象运算符 3.8+
if (n := len(s)) > 10:
    print(n)

# 匹配 3.10+
match cmd:
    case "start" | "up": ...
    case [x, y]: ...
    case {"type": t}: ...
    case _: ...
```

## 推导式

```python
[x*2 for x in range(5)]
[x for x in range(20) if x % 2 == 0]
{x for x in "hello"}
{k: v for k, v in pairs}
(x for x in range(10))              # 生成器，惰性
[[i*j for j in range(3)] for i in range(3)]
```

## 函数

```python
def add(a, b=1, *args, **kwargs):
    """文档字符串"""
    return a + b

add(1); add(1, 2)
add(*[1,2]); add(**{"a":1,"b":2})

def f(x: int, y: int = 0) -> int:   # 类型注解
    return x + y

def good(items=None):
    if items is None: items = []
    return items
# 默认参数勿用可变对象（[]、{}）

square = lambda x: x * x

def f(*, key, opt=0):    # 关键字专用参数
    ...

def stats(*nums):
    return sum(nums), len(nums)
```

内置高阶：

```python
list(map(lambda x: x*2, [1,2,3]))
list(filter(lambda x: x>1, [1,2,3]))
from functools import reduce
reduce(lambda a,b: a+b, [1,2,3])
sorted(data, key=lambda d: d["age"], reverse=True)
max(items, key=len)
```

## 函数进阶

```python
# 闭包
def counter():
    c = 0
    def inc():
        nonlocal c
        c += 1
        return c
    return inc

# 装饰器
import functools
def log(fn):
    @functools.wraps(fn)
    def wrapper(*a, **k):
        print("call", fn.__name__)
        return fn(*a, **k)
    return wrapper

@log
def hello(): ...

# 生成器（惰性、省内存）
def gen(n):
    for i in range(n):
        yield i

def read_big(path):
    with open(path) as f:
        for line in f:
            yield line.strip()

# 生成器表达式
total = sum(x*x for x in range(1000))

# 偏函数
from functools import partial
add10 = partial(lambda a, b: a+b, 10)
```

## 类与对象

```python
class Animal:
    species = "动物"                 # 类属性（所有实例共享）

    def __init__(self, name):        # 构造
        self.name = name             # 实例属性

    def speak(self):                 # 实例方法
        return f"{self.name} 叫"

    @property                        # 只读属性
    def label(self):
        return f"<{self.name}>"

    @staticmethod
    def kind():                      # 静态方法
        return "动物"

    @classmethod
    def from_dict(cls, d):           # 类方法（替代构造）
        return cls(d["name"])

    def __str__(self): return f"Animal({self.name})"   # print
    def __repr__(self): return f"Animal({self.name!r})" # 调试
    def __len__(self): return len(self.name)
    def __eq__(self, o): return isinstance(o, Animal) and o.name == self.name

class Dog(Animal):                   # 继承
    def __init__(self, name, breed):
        super().__init__(name)
        self.breed = breed
    def speak(self):                 # 重写
        return super().speak() + " 汪汪"

d = Dog("旺财", "柴犬")
isinstance(d, Animal)     # True
```

## 面向对象进阶

```python
# 数据类
from dataclasses import dataclass, field
@dataclass
class Point:
    x: int
    y: int = 0
    tags: list = field(default_factory=list)

# 枚举
from enum import Enum, auto
class Color(Enum):
    RED = auto()
    GREEN = auto()
Color.RED.name; Color.RED.value

# 抽象基类
from abc import ABC, abstractmethod
class Shape(ABC):
    @abstractmethod
    def area(self): ...

# 魔术方法常用：__init__ __str__ __repr__ __eq__ __lt__ __hash__
#              __len__ __iter__ __next__ __getitem__ __call__ __enter__ __exit__
```

## 异常处理

```python
try:
    risky()
except ValueError as e:
    print("值错误", e)
except (KeyError, IndexError):
    ...
except Exception as e:
    print("其他", e)
else:
    print("无异常")
finally:
    print("总会执行")

raise ValueError("非法参数")
assert x > 0, "x 必须为正"

# 异常链
try:
    ...
except OSError as e:
    raise RuntimeError("处理失败") from e

class MyError(Exception):
    pass
```

## 上下文管理器

```python
with open("a.txt", encoding="utf-8") as f:
    data = f.read()      # 自动关闭

# 自定义
from contextlib import contextmanager
@contextmanager
def timer():
    import time
    t = time.perf_counter()
    try:
        yield
    finally:
        print(time.perf_counter() - t)

with timer():
    work()

# 多资源
with open("in") as fi, open("out","w") as fo:
    fo.write(fi.read())
```

## 文件与路径

```python
with open("a.txt", "r", encoding="utf-8") as f:
    text = f.read()
    f.seek(0); lines = f.readlines()
with open("out.txt", "w", encoding="utf-8") as f:
    f.write("内容")
with open("data.csv") as f:
    for line in f: ...
with open("img.png", "rb") as f:
    data = f.read()

# pathlib（推荐）
from pathlib import Path
p = Path("dir/file.txt")
p.exists(); p.is_file(); p.is_dir(); p.name; p.stem; p.suffix; p.parent; p.parts
p.read_text(encoding="utf-8"); p.write_text("hi", encoding="utf-8")
Path("out").mkdir(parents=True, exist_ok=True)
list(Path(".").glob("*.py")); list(Path(".").rglob("*.py"))
p.stat().st_size
```

模式：`r` 读、`w` 覆盖、`a` 追加、`x` 独占、`b` 二进制、`+` 读写、`t` 文本。

## 常用内置函数

`len print input type isinstance int float str bool list tuple dict set` ·
`range enumerate zip map filter sorted reversed sum min max abs round` ·
`any all` · `open` · `repr id hash` · `getattr setattr hasattr delattr` ·
`iter next` · `callable` · `vars dir help` · `bin hex oct ord chr` · `divmod pow`.

## 常用标准库

```python
import os, sys, shutil, subprocess, glob
os.getcwd(); os.listdir("."); os.makedirs("a/b", exist_ok=True)
os.environ.get("HOME"); os.path.join("a","b"); os.path.exists("f")
sys.argv; sys.exit(0); sys.path
shutil.copy("a","b"); shutil.rmtree("d"); shutil.move("a","b")
subprocess.run(["ls","-l"], check=True, capture_output=True, text=True)
glob.glob("**/*.py", recursive=True)

from collections import Counter, defaultdict, deque, OrderedDict, namedtuple
Counter("hello").most_common(2)
dd = defaultdict(list); dd["k"].append(1)
q = deque([1,2]); q.appendleft(0); q.pop()
Point = namedtuple("Point", "x y"); Point(1,2).x

from itertools import chain, combinations, permutations, product, groupby, islice
list(chain([1,2],[3]))
list(combinations([1,2,3], 2))
list(permutations([1,2], 2))
list(product([0,1], repeat=2))
list(islice(range(100), 5))

import random, math, statistics, uuid, hashlib, base64, csv, argparse, logging
random.randint(1,6); random.choice(lst); random.shuffle(lst); random.sample(lst, 2)
math.sqrt(2); math.pi; math.floor(1.9); math.gcd(12,18)
statistics.mean([1,2,3]); statistics.median([1,2,3])
uuid.uuid4().hex
hashlib.sha256(b"data").hexdigest()
base64.b64encode(b"data").decode()

# CSV
import csv
with open("d.csv", newline="", encoding="utf-8") as f:
    for row in csv.DictReader(f): print(row["name"])
with open("o.csv","w",newline="",encoding="utf-8") as f:
    w = csv.writer(f); w.writerow(["a","b"])

# 命令行参数
import argparse
p = argparse.ArgumentParser()
p.add_argument("name"); p.add_argument("-n", type=int, default=1)
args = p.parse_args()

# 日志
import logging
logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
logging.info("hello")
```

## 正则表达式

```python
import re
re.search(r"\d+", "abc123").group()      # 123
re.match(r"^a", s)                        # 从开头匹配
re.fullmatch(r"\w+", s)                   # 整串匹配
re.findall(r"\d+", "a1b22")               # ['1','22']
re.finditer(r"\w+", s)
re.sub(r"\s+", " ", s)                    # 替换
re.sub(r"(\w+)@(\w+)", r"\2#\1", s)      # 分组替换
re.split(r"[,;]", s)
re.compile(r"^[A-Z]", re.I)               # 预编译 + 忽略大小写

m = re.search(r"(\d{4})-(\d{2})", "2024-05")
m.group(1), m.group(2), m.groups()
re.search(r"(?P<y>\d{4})", "2024").group("y")

# 元字符：. \d \D \w \W \s \S \b ^ $ * + ? {n,m} [] () | \
# 贪婪/非贪婪：.* 与 .*?；标志：re.I re.M re.S re.X
```

## JSON 与日期时间

```python
import json
d = {"a":1, "b":[1,2]}
json.dumps(d, ensure_ascii=False, indent=2)
json.loads('{"a":1}')
with open("d.json", encoding="utf-8") as f: obj = json.load(f)

from datetime import datetime, date, time, timedelta
now = datetime.now()
now.strftime("%Y-%m-%d %H:%M:%S")
datetime.strptime("2024-05-01", "%Y-%m-%d")
now + timedelta(days=1, hours=2)
now.year, now.month, now.day, now.date(), now.timestamp()
datetime.fromtimestamp(0)
```

## 类型注解

```python
from typing import Optional, Union, List, Dict, Tuple, Set, Callable, Any, Iterable
def greet(name: str, times: int = 1) -> str:
    return name * times
x: int = 1
names: List[str] = []
m: Dict[str, int] = {}
maybe: Optional[str] = None          # 或 str | None（3.10+）
fn: Callable[[int], int] = lambda n: n
def f(x: int | None) -> None: ...    # 3.10+
```

## 异步 asyncio

```python
import asyncio
async def fetch(n):
    await asyncio.sleep(1)
    return n * 2

async def main():
    a, b = await asyncio.gather(fetch(1), fetch(2))
    print(a, b)

asyncio.run(main())

# 异步迭代
async def agen():
    for i in range(3):
        yield i
async for x in agen(): print(x)
```

## 虚拟环境与工具

```bash
python -m venv .venv
source .venv/bin/activate      # Windows: .venv\Scripts\activate
pip install requests
pip freeze > requirements.txt
pip install -r requirements.txt
deactivate
```

格式化/检查：`black`、`ruff`、`flake8`、`mypy`。

## 实用技巧与坑

```python
a, b = b, a
first, *rest = [1,2,3,4]
d = d1 | d2                        # 字典合并 3.9+
val = (data or {}).get("k")        # 安全链式取值
if __name__ == "__main__": main()  # 入口

import time
t = time.perf_counter(); work(); print(time.perf_counter()-t)

from collections import Counter
Counter(words).most_common(10)     # 词频
```

- 默认参数勿用可变对象；类属性与实例属性区分清楚。
- 浅拷贝 `copy.copy` / 深拷贝 `copy.deepcopy`。
- 浮点精度：金额用 `decimal.Decimal`；比较浮点用 `math.isclose`。
- 字符串拼接大量时用 `"".join(...)` 而非 `+=`。
- 迭代时不要修改列表；用副本或推导式新建。
- 文件读写指定 `encoding="utf-8"`，避免 Windows 默认 GBK。
- 命名：`snake_case` 变量/函数、`PascalCase` 类、`UPPER` 常量；遵循 PEP 8。
