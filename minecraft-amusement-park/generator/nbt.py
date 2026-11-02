"""Minimal, dependency-free NBT writer (big-endian, Java edition).

Python values map to tags like this:
    dict           -> TAG_Compound
    str            -> TAG_String
    int            -> TAG_Int
    Byte/Short/Long/Float/Double wrappers -> the matching numeric tag
    List(tag, [...]) -> TAG_List of the given element tag id
    IntArray / LongArray (numpy arrays) -> TAG_Int_Array / TAG_Long_Array
"""
import gzip
import struct

import numpy as np

TAG_END, TAG_BYTE, TAG_SHORT, TAG_INT, TAG_LONG, TAG_FLOAT, TAG_DOUBLE = 0, 1, 2, 3, 4, 5, 6
TAG_BYTE_ARRAY, TAG_STRING, TAG_LIST, TAG_COMPOUND, TAG_INT_ARRAY, TAG_LONG_ARRAY = 7, 8, 9, 10, 11, 12


class Byte(int):
    pass


class Short(int):
    pass


class Long(int):
    pass


class Float(float):
    pass


class Double(float):
    pass


class List:
    def __init__(self, tag, items=()):
        self.tag = tag
        self.items = list(items)


class IntArray:
    def __init__(self, arr):
        self.arr = np.asarray(arr, dtype=np.int64).astype('>i4')


class LongArray:
    def __init__(self, arr):
        self.arr = np.asarray(arr).astype('>i8')


def _tag_of(v):
    if isinstance(v, bool):
        return TAG_BYTE
    if isinstance(v, Byte):
        return TAG_BYTE
    if isinstance(v, Short):
        return TAG_SHORT
    if isinstance(v, Long):
        return TAG_LONG
    if isinstance(v, (int, np.integer)):
        return TAG_INT
    if isinstance(v, Double):
        return TAG_DOUBLE
    if isinstance(v, (Float, float)):
        return TAG_FLOAT
    if isinstance(v, str):
        return TAG_STRING
    if isinstance(v, List):
        return TAG_LIST
    if isinstance(v, dict):
        return TAG_COMPOUND
    if isinstance(v, IntArray):
        return TAG_INT_ARRAY
    if isinstance(v, LongArray):
        return TAG_LONG_ARRAY
    raise TypeError(f"Cannot encode {type(v)} as NBT")


def _mutf8(s):
    # Modified UTF-8 as used by Java's DataOutputStream.writeUTF.
    out = bytearray()
    for ch in s:
        c = ord(ch)
        if 0 < c < 0x80:
            out.append(c)
        elif c < 0x800:
            out += bytes([0xC0 | (c >> 6), 0x80 | (c & 0x3F)])
        elif c < 0x10000:
            out += bytes([0xE0 | (c >> 12), 0x80 | ((c >> 6) & 0x3F), 0x80 | (c & 0x3F)])
        else:  # encode as surrogate pair, each surrogate as 3 bytes
            c -= 0x10000
            for sur in (0xD800 | (c >> 10), 0xDC00 | (c & 0x3FF)):
                out += bytes([0xE0 | (sur >> 12), 0x80 | ((sur >> 6) & 0x3F), 0x80 | (sur & 0x3F)])
    return bytes(out)


def _write_str(buf, s):
    b = _mutf8(s)
    buf.append(struct.pack('>H', len(b)))
    buf.append(b)


def _write_payload(buf, tag, v):
    if tag == TAG_BYTE:
        buf.append(struct.pack('>b', int(v)))
    elif tag == TAG_SHORT:
        buf.append(struct.pack('>h', int(v)))
    elif tag == TAG_INT:
        buf.append(struct.pack('>i', int(v)))
    elif tag == TAG_LONG:
        buf.append(struct.pack('>q', int(v)))
    elif tag == TAG_FLOAT:
        buf.append(struct.pack('>f', float(v)))
    elif tag == TAG_DOUBLE:
        buf.append(struct.pack('>d', float(v)))
    elif tag == TAG_STRING:
        _write_str(buf, v)
    elif tag == TAG_LIST:
        buf.append(struct.pack('>bi', v.tag if v.items else TAG_END, len(v.items)))
        for it in v.items:
            _write_payload(buf, v.tag, it)
    elif tag == TAG_COMPOUND:
        for k, val in v.items():
            t = _tag_of(val)
            buf.append(struct.pack('>b', t))
            _write_str(buf, k)
            _write_payload(buf, t, val)
        buf.append(b'\x00')
    elif tag == TAG_INT_ARRAY:
        buf.append(struct.pack('>i', v.arr.size))
        buf.append(v.arr.tobytes())
    elif tag == TAG_LONG_ARRAY:
        buf.append(struct.pack('>i', v.arr.size))
        buf.append(v.arr.tobytes())
    else:
        raise ValueError(tag)


def encode(root, name=''):
    buf = [struct.pack('>b', TAG_COMPOUND)]
    _write_str(buf, name)
    _write_payload(buf, TAG_COMPOUND, root)
    return b''.join(buf)


def write_gzip(path, root, name=''):
    data = encode(root, name)
    with gzip.open(path, 'wb', compresslevel=9) as f:
        f.write(data)
    return len(data)
